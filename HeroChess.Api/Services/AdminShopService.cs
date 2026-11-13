// Vai trò file: Quản trị cửa hàng: bật/tắt bán, đổi giá, lịch sử mua. Mọi thay đổi ghi audit trong cùng transaction.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class AdminShopService(AppDbContext db, AdminAuditWriter audit, AdminCatalogService catalog)
{
    // ListAsync: Danh sách hero trong cửa hàng kèm cờ mua được và số lượt đã mua.
    public async Task<AdminShopHeroPageDto> ListAsync(string? query, bool? inShop, string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var heroes = db.Heroes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();
            heroes = heroes.Where(x => x.Name.Contains(text) || x.Code.Contains(text));
        }
        if (inShop.HasValue) heroes = heroes.Where(x => x.IsInShop == inShop.Value);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var afterCode = CursorCodec.ParseText(cursor);
            // CompareTo dịch được thành `code > @p` trong PostgreSQL; cùng thứ tự với ORDER BY nên cursor khớp.
            heroes = heroes.Where(x => x.Code.CompareTo(afterCode) > 0);
        }
        var total = await heroes.CountAsync(ct);
        var rows = await heroes.OrderBy(x => x.Code).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var purchases = await PurchaseStatsAsync(page.Select(x => x.Id), ct);
        var heroDtos = await catalog.BuildHeroesAsync(page, ct);
        var items = page.Select((hero, index) =>
        {
            purchases.TryGetValue(hero.Id, out var stats);
            return new AdminShopHeroDto(heroDtos[index], hero.IsEnabled && hero.IsInShop && hero.CoinPrice > 0,
                stats.Count, stats.Last);
        }).ToArray();
        string? next = null;
        if (rows.Count > limit) next = CursorCodec.FormatText(page[^1].Code);
        return new(items, next, total);
    }

    // UpdateAsync: Đổi giá và/hoặc bật/tắt bán; giá là chuỗi số nguyên >= 0.
    public async Task<AdminHeroDto> UpdateAsync(Guid actorId, Guid heroId, AdminShopUpdateRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hero = await db.Heroes.FromSqlInterpolated($"SELECT * FROM hero_chess.hero WHERE id={heroId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "HERO_NOT_FOUND", "The hero was not found.");
        var before = new { hero.CoinPrice, hero.IsInShop };
        if (request.CoinPrice is not null)
        {
            var price = AdminValidation.ParseCoinPrice(request.CoinPrice);
            if (price != hero.CoinPrice)
                audit.Write(actorId, "hero.price.update", "hero", heroId.ToString("D"),
                    new { coinPrice = hero.CoinPrice.ToString() }, new { coinPrice = price.ToString() }, request.Reason);
            hero.CoinPrice = price;
        }
        if (request.IsInShop.HasValue && request.IsInShop.Value != hero.IsInShop)
        {
            audit.Write(actorId, "hero.shop.set", "hero", heroId.ToString("D"),
                new { isInShop = hero.IsInShop }, new { isInShop = request.IsInShop.Value }, request.Reason);
            hero.IsInShop = request.IsInShop.Value;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await catalog.BuildHeroesAsync([hero], ct))[0];
    }

    // BatchUpdatePricesAsync: Đổi giá nhiều hero trong một transaction; dòng lỗi được báo riêng, không làm mất dòng đã đổi.
    public async Task<AdminBatchPriceResultDto> BatchUpdatePricesAsync(Guid actorId, AdminBatchPriceRequest request, CancellationToken ct)
    {
        if (request.Items.Count == 0) throw new ApiException(400, "EMPTY_BATCH", "At least one item is required.");
        if (request.Items.Count > 200) throw new ApiException(400, "BATCH_TOO_LARGE", "A batch may contain at most 200 items.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var results = new List<AdminBatchPriceItemResult>();
        foreach (var item in request.Items)
        {
            try
            {
                var price = AdminValidation.ParseCoinPrice(item.CoinPrice);
                var hero = await db.Heroes.FromSqlInterpolated($"SELECT * FROM hero_chess.hero WHERE id={item.HeroId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct);
                if (hero is null) throw new ApiException(404, "HERO_NOT_FOUND", "The hero was not found.");
                if (hero.CoinPrice != price)
                {
                    audit.Write(actorId, "hero.price.update", "hero", hero.Id.ToString("D"),
                        new { coinPrice = hero.CoinPrice.ToString() }, new { coinPrice = price.ToString() }, request.Reason);
                    hero.CoinPrice = price;
                }
                results.Add(new(item.HeroId, true, price.ToString(), null, null));
            }
            catch (ApiException error)
            {
                results.Add(new(item.HeroId, false, null, error.Code, error.Message));
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(results);
    }

    // PurchasesAsync: Lịch sử mua hero theo thời gian, có phân trang keyset.
    public async Task<AdminPurchasePageDto> PurchasesAsync(Guid? heroId, string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = db.CoinTransactions.AsNoTracking().Where(x => x.Kind == "hero_purchase");
        if (heroId is not null) query = query.Where(x => x.HeroId == heroId);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var (at, id) = CursorCodec.Parse(cursor);
            query = query.Where(x => x.CreatedAt < at || (x.CreatedAt == at && x.Id.CompareTo(id) < 0));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var playerIds = page.Where(x => x.PlayerId != Guid.Empty).Select(x => x.PlayerId).Distinct().ToArray();
        var names = await db.Players.AsNoTracking().Where(x => playerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var heroIds = page.Where(x => x.HeroId is not null).Select(x => x.HeroId!.Value).Distinct().ToArray();
        var heroCodes = await db.Heroes.AsNoTracking().Where(x => heroIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var items = page.Select(x => new AdminPurchaseDto(x.Id, x.PlayerId, names.GetValueOrDefault(x.PlayerId), x.HeroId ?? Guid.Empty,
            x.HeroId is not null ? heroCodes.GetValueOrDefault(x.HeroId.Value) : null, x.Amount, x.CreatedAt)).ToArray();
        string? next = null;
        if (rows.Count > limit) next = CursorCodec.Format(page[^1].CreatedAt, page[^1].Id);
        return new(items, next, total);
    }

    // PurchaseStatsAsync: Đếm số lượt mua theo hero; nguồn là ledger, không phải bảng đếm phụ.
    private async Task<Dictionary<Guid, (long Count, DateTimeOffset Last)>> PurchaseStatsAsync(IEnumerable<Guid> heroIds, CancellationToken ct)
    {
        var ids = heroIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var rows = await db.CoinTransactions.AsNoTracking().Where(x => x.Kind == "hero_purchase" && x.HeroId != null && ids.Contains(x.HeroId!.Value))
            .GroupBy(x => x.HeroId!.Value)
            .Select(g => new { HeroId = g.Key, Count = g.LongCount(), Last = g.Max(x => x.CreatedAt) })
            .ToListAsync(ct);
        return rows.ToDictionary(x => x.HeroId, x => (x.Count, x.Last));
    }
}

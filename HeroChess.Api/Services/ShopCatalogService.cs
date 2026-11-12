// Vai trò file: API cửa hàng cho người chơi: danh sách hero bán, hero đang sở hữu và lịch sử mua. Mọi thay đổi số dư đi qua transaction.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class ShopCatalogService(AppDbContext db)
{
    // ListAsync: Hero đang bán: bật, có trong shop và giá > 0 (giá 0 nghĩa là miễn phí nên không hiện trong cửa hàng).
    public async Task<IReadOnlyList<HeroDto>> ListAsync(Guid playerId, CancellationToken ct)
    {
        var heroes = await db.Heroes.AsNoTracking().Where(x => x.IsEnabled && x.IsInShop && x.CoinPrice > 0)
            .OrderBy(x => x.ClassCode).ThenBy(x => x.Code).ToListAsync(ct);
        if (heroes.Count == 0) return [];
        var ids = heroes.Select(x => x.Id).ToArray();
        var links = await db.HeroFactions.AsNoTracking().Where(x => ids.Contains(x.HeroId)).ToListAsync(ct);
        var owned = await db.PlayerHeroes.AsNoTracking().Where(x => x.PlayerId == playerId && ids.Contains(x.HeroId))
            .Select(x => x.HeroId).ToHashSetAsync(ct);
        return heroes.Select(x => new HeroDto(x.Id, x.Code, x.Name, x.CharacterId, x.ClassCode, x.SetupPoints, null,
            links.Where(l => l.HeroId == x.Id).Select(l => l.FactionId).ToArray(), x.AssetKey, owned.Contains(x.Id), x.CoinPrice.ToString())).ToArray();
    }

    // OwnedAsync: Hero người chơi đang sở hữu kèm nguồn nhận.
    public async Task<IReadOnlyList<AdminOwnedHeroDto>> OwnedAsync(Guid playerId, CancellationToken ct)
    {
        return await db.PlayerHeroes.AsNoTracking().Where(x => x.PlayerId == playerId).OrderBy(x => x.AcquiredAt)
            .Join(db.Heroes.AsNoTracking(), x => x.HeroId, h => h.Id, (ph, h) => new AdminOwnedHeroDto(h.Id, h.Code, h.Name, ph.AcquiredVia, ph.AcquiredAt))
            .ToListAsync(ct);
    }

    // PurchasesAsync: Lịch sử mua hero của người chơi.
    public async Task<IReadOnlyList<AdminPurchaseDto>> PurchasesAsync(Guid playerId, CancellationToken ct)
    {
        var rows = await db.CoinTransactions.AsNoTracking().Where(x => x.PlayerId == playerId && x.Kind == "hero_purchase")
            .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
        var heroIds = rows.Where(x => x.HeroId is not null).Select(x => x.HeroId!.Value).Distinct().ToArray();
        var codes = await db.Heroes.AsNoTracking().Where(x => heroIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        return rows.Select(x => new AdminPurchaseDto(x.Id, x.PlayerId, null, x.HeroId ?? Guid.Empty,
            x.HeroId is not null ? codes.GetValueOrDefault(x.HeroId.Value) : null, x.Amount, x.CreatedAt)).ToArray();
    }
}

// Vai trò file: Admin đã triển khai đổi giá hero/cosmetic và đọc audit. Chưa có API khóa player hay chỉnh SP/skill.
using System.Globalization;
using System.Text;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class AdminService(AppDbContext db, TimeProvider clock)
{
    // UpdateHeroPriceAsync: Chuyển yêu cầu đổi giá hero vào logic transaction chung.
    public Task<AdminPriceDto> UpdateHeroPriceAsync(Guid actorId, Guid id, UpdatePriceRequest request, CancellationToken ct) =>
        UpdatePrice(actorId, id, "hero", request, ct);
    // UpdateCosmeticPriceAsync: Chuyển yêu cầu đổi giá cosmetic vào logic transaction chung.
    public Task<AdminPriceDto> UpdateCosmeticPriceAsync(Guid actorId, Guid id, UpdatePriceRequest request, CancellationToken ct) =>
        UpdatePrice(actorId, id, "cosmetic", request, ct);

    // UpdatePrice: Parse coinPrice, lock nội dung, lưu giá trước/sau và lý do vào audit cùng transaction.
    private async Task<AdminPriceDto> UpdatePrice(Guid actorId, Guid id, string entityType, UpdatePriceRequest request, CancellationToken ct)
    {
        if (!long.TryParse(request.CoinPrice, NumberStyles.None, CultureInfo.InvariantCulture, out var price) || price < 0)
            throw new ApiException(400, "INVALID_COIN_PRICE", "coinPrice must be a non-negative integer string.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        long before;
        if (entityType == "hero")
        {
            var entity = await db.Heroes.FromSqlInterpolated($"SELECT * FROM hero_chess.hero WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(ct)
                ?? throw new ApiException(404, "HERO_NOT_FOUND", "The hero was not found.");
            before = entity.CoinPrice; entity.CoinPrice = price;
        }
        else
        {
            var entity = await db.Cosmetics.FromSqlInterpolated($"SELECT * FROM hero_chess.cosmetic WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(ct)
                ?? throw new ApiException(404, "COSMETIC_NOT_FOUND", "The cosmetic was not found.");
            before = entity.CoinPrice; entity.CoinPrice = price;
        }
        var now = clock.GetUtcNow();
        db.AdminAuditLogs.Add(new AdminAuditLog { Id = Guid.NewGuid(), ActorUserId = actorId, Action = "price.update", EntityType = entityType,
            EntityId = id.ToString("D"), BeforeData = GameJson.Document(new { coinPrice = before.ToString() }),
            AfterData = GameJson.Document(new { coinPrice = price.ToString() }), Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), CreatedAt = now });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(id, entityType, price.ToString(), now);
    }

    // AuditAsync: Đọc audit theo cursor thời gian/ID, giới hạn số bản ghi mỗi trang.
    public async Task<AdminAuditPageDto> AuditAsync(string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        var query = db.AdminAuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            string[] parts;
            try { parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|'); }
            catch (FormatException) { throw new ApiException(400, "INVALID_CURSOR", "The cursor is invalid."); }
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var id))
                throw new ApiException(400, "INVALID_CURSOR", "The cursor is invalid.");
            var at = new DateTimeOffset(ticks, TimeSpan.Zero);
            query = query.Where(x => x.CreatedAt < at || (x.CreatedAt == at && x.Id.CompareTo(id) < 0));
        }
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var items = rows.Take(limit).Select(x => new AdminAuditDto(x.Id, x.ActorUserId, x.Action, x.EntityType, x.EntityId,
            x.BeforeData?.RootElement.Clone(), x.AfterData?.RootElement.Clone(), x.Reason, x.CreatedAt)).ToArray();
        string? next = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1]; next = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.CreatedAt.UtcTicks}|{last.Id}"));
        }
        return new(items, next);
    }
}

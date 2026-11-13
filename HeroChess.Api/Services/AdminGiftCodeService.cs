// Vai trò file: Gift code: admin tạo/sửa, người chơi quy đổi. Quy đổi chạy trong transaction với row lock để chống quy đổi trùng.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class AdminGiftCodeService(AppDbContext db, AdminAuditWriter audit, TimeProvider clock)
{
    // ListAsync: Danh sách gift code kèm số lượt đã quy đổi; lọc theo trạng thái và tìm theo mã.
    public async Task<AdminGiftCodePageDto> ListAsync(string? query, bool? active, string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var codes = db.GiftCodes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query)) codes = codes.Where(x => x.Code.Contains(query.Trim()));
        if (active.HasValue) codes = codes.Where(x => x.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var (at, id) = CursorCodec.Parse(cursor);
            codes = codes.Where(x => x.CreatedAt < at || (x.CreatedAt == at && x.Id.CompareTo(id) < 0));
        }
        var total = await codes.CountAsync(ct);
        var rows = await codes.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var counts = await RedemptionCountsAsync(page.Select(x => x.Id), ct);
        var items = page.Select(x => ToDto(x, counts.GetValueOrDefault(x.Id))).ToArray();
        string? next = null;
        if (rows.Count > limit) next = CursorCodec.Format(page[^1].CreatedAt, page[^1].Id);
        return new(items, next, total);
    }

    // DetailAsync: Chi tiết một gift code.
    public async Task<AdminGiftCodeDto> DetailAsync(Guid giftCodeId, CancellationToken ct)
    {
        var code = await db.GiftCodes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == giftCodeId, ct)
            ?? throw new ApiException(404, "GIFT_CODE_NOT_FOUND", "The gift code was not found.");
        var counts = await RedemptionCountsAsync([code.Id], ct);
        return ToDto(code, counts.GetValueOrDefault(code.Id));
    }

    // CreateAsync: Tạo gift code; mã để trống thì server sinh mã ngẫu nhiên không trùng.
    public async Task<AdminGiftCodeDto> CreateAsync(Guid actorId, AdminGiftCodeUpsertRequest request, CancellationToken ct)
    {
        var kind = request.RewardKind?.Trim().ToLowerInvariant();
        if (kind is not ("coin" or "hero" or "cosmetic"))
            throw new ApiException(400, "INVALID_REWARD_KIND", "rewardKind must be 'coin', 'hero' or 'cosmetic'.");
        if (request.RewardQuantity <= 0) throw new ApiException(400, "INVALID_REWARD_QUANTITY", "rewardQuantity must be positive.");
        if (request.MaxRedemptions <= 0) throw new ApiException(400, "INVALID_MAX_REDEMPTIONS", "maxRedemptions must be positive.");
        if (request.MaxPerPlayer <= 0) throw new ApiException(400, "INVALID_MAX_PER_PLAYER", "maxPerPlayer must be positive.");
        if (request.ExpiresAt is not null && request.StartsAt is not null && request.ExpiresAt <= request.StartsAt)
            throw new ApiException(400, "INVALID_EXPIRY", "expiresAt must be after startsAt.");
        if (kind == "hero")
        {
            if (request.RewardHeroId is null) throw new ApiException(400, "REWARD_HERO_REQUIRED", "rewardHeroId is required for hero rewards.");
            if (!await db.Heroes.AsNoTracking().AnyAsync(x => x.Id == request.RewardHeroId, ct))
                throw new ApiException(422, "HERO_NOT_FOUND", "The hero was not found.");
        }
        if (kind == "cosmetic")
        {
            if (request.RewardCosmeticId is null) throw new ApiException(400, "REWARD_COSMETIC_REQUIRED", "rewardCosmeticId is required for cosmetic rewards.");
            if (!await db.Cosmetics.AsNoTracking().AnyAsync(x => x.Id == request.RewardCosmeticId, ct))
                throw new ApiException(422, "COSMETIC_NOT_FOUND", "The cosmetic was not found.");
        }
        if (kind == "coin" && request.RewardHeroId is not null || kind == "coin" && request.RewardCosmeticId is not null)
            throw new ApiException(400, "INVALID_REWARD_TARGET", "Coin rewards cannot reference a hero or cosmetic.");

        var code = request.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            do { code = NewCode(); }
            while (await db.GiftCodes.AsNoTracking().AnyAsync(x => x.Code == code, ct));
        }
        else
        {
            if (code.Length > 64) throw new ApiException(400, "CODE_TOO_LONG", "The code must not exceed 64 characters.");
            if (await db.GiftCodes.AsNoTracking().AnyAsync(x => x.Code == code, ct))
                throw new ApiException(409, "GIFT_CODE_EXISTS", "A gift code with this code already exists.");
        }

        var now = clock.GetUtcNow();
        var row = new GiftCode
        {
            Id = Guid.NewGuid(), Code = code, RewardKind = kind, RewardQuantity = request.RewardQuantity,
            RewardHeroId = kind == "hero" ? request.RewardHeroId : null,
            RewardCosmeticId = kind == "cosmetic" ? request.RewardCosmeticId : null,
            StartsAt = request.StartsAt ?? now, ExpiresAt = request.ExpiresAt,
            MaxRedemptions = request.MaxRedemptions, MaxPerPlayer = request.MaxPerPlayer,
            IsActive = request.IsActive ?? true, CreatedBy = actorId, CreatedAt = now, UpdatedAt = now
        };
        db.GiftCodes.Add(row);
        audit.Write(actorId, "giftcode.create", "gift_code", row.Id.ToString("D"), null,
            new { row.Code, row.RewardKind, row.RewardQuantity, row.MaxRedemptions, row.MaxPerPlayer }, request.Reason);
        await db.SaveChangesAsync(ct);
        return ToDto(row, 0);
    }

    // UpdateAsync: Sửa cấu hình gift code; không cho giảm maxRedemptions xuống dưới số lượt đã quy đổi.
    public async Task<AdminGiftCodeDto> UpdateAsync(Guid actorId, Guid giftCodeId, AdminGiftCodeUpdateRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.GiftCodes.FromSqlInterpolated($"SELECT * FROM hero_chess.gift_code WHERE id={giftCodeId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "GIFT_CODE_NOT_FOUND", "The gift code was not found.");
        var before = new { row.IsActive, row.ExpiresAt, row.MaxRedemptions, row.MaxPerPlayer };
        if (request.IsActive.HasValue) row.IsActive = request.IsActive.Value;
        if (request.ExpiresAt.HasValue) row.ExpiresAt = request.ExpiresAt;
        if (request.MaxRedemptions.HasValue)
        {
            if (request.MaxRedemptions.Value <= 0) throw new ApiException(400, "INVALID_MAX_REDEMPTIONS", "maxRedemptions must be positive.");
            var used = await db.GiftCodeRedemptions.AsNoTracking().CountAsync(x => x.GiftCodeId == giftCodeId, ct);
            if (request.MaxRedemptions.Value < used)
                throw new ApiException(409, "MAX_REDEMPTIONS_BELOW_USED", "maxRedemptions cannot be lower than the number of existing redemptions.");
            row.MaxRedemptions = request.MaxRedemptions.Value;
        }
        if (request.MaxPerPlayer.HasValue)
        {
            if (request.MaxPerPlayer.Value <= 0) throw new ApiException(400, "INVALID_MAX_PER_PLAYER", "maxPerPlayer must be positive.");
            row.MaxPerPlayer = request.MaxPerPlayer.Value;
        }
        row.UpdatedAt = clock.GetUtcNow();
        var after = new { row.IsActive, row.ExpiresAt, row.MaxRedemptions, row.MaxPerPlayer };
        audit.Write(actorId, "giftcode.update", "gift_code", giftCodeId.ToString("D"), before, after, request.Reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        var counts = await RedemptionCountsAsync([giftCodeId], ct);
        return ToDto(row, counts.GetValueOrDefault(giftCodeId));
    }

    // RedemptionsAsync: Lịch sử quy đổi của một gift code.
    public async Task<AdminGiftCodeRedemptionPageDto> RedemptionsAsync(Guid giftCodeId, string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = db.GiftCodeRedemptions.AsNoTracking().Where(x => x.GiftCodeId == giftCodeId);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var (at, id) = CursorCodec.Parse(cursor);
            query = query.Where(x => x.RedeemedAt < at || (x.RedeemedAt == at && x.Id.CompareTo(id) < 0));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.RedeemedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var names = await db.Players.AsNoTracking().Where(x => page.Select(p => p.PlayerId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var items = page.Select(x => new AdminGiftCodeRedemptionDto(x.Id, x.PlayerId, names.GetValueOrDefault(x.PlayerId),
            x.RewardKind, x.RewardQuantity, x.CoinBalanceAfter, x.RedeemedAt)).ToArray();
        string? next = null;
        if (rows.Count > limit) next = CursorCodec.Format(page[^1].RedeemedAt, page[^1].Id);
        return new(items, next, total);
    }

    // RedeemAsync: Người chơi quy đổi mã. Lock gift_code để serialize, kiểm tra hạn dùng và trao thưởng trong một transaction.
    public async Task<RedeemGiftCodeDto> RedeemAsync(Guid playerId, RedeemGiftCodeRequest request, CancellationToken ct)
    {
        var code = request.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code)) throw new ApiException(400, "CODE_REQUIRED", "A gift code is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.GiftCodes.FromSqlInterpolated($"SELECT * FROM hero_chess.gift_code WHERE code={code} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "GIFT_CODE_NOT_FOUND", "The gift code was not found.");
        var now = clock.GetUtcNow();
        if (!row.IsActive) throw new ApiException(409, "GIFT_CODE_INACTIVE", "The gift code is not active.");
        if (now < row.StartsAt) throw new ApiException(409, "GIFT_CODE_NOT_STARTED", "The gift code is not active yet.");
        if (row.ExpiresAt is not null && now > row.ExpiresAt) throw new ApiException(409, "GIFT_CODE_EXPIRED", "The gift code has expired.");
        var used = await db.GiftCodeRedemptions.AsNoTracking().CountAsync(x => x.GiftCodeId == row.Id, ct);
        if (used >= row.MaxRedemptions) throw new ApiException(409, "GIFT_CODE_EXHAUSTED", "The gift code has reached its redemption limit.");
        var usedByPlayer = await db.GiftCodeRedemptions.AsNoTracking().CountAsync(x => x.GiftCodeId == row.Id && x.PlayerId == playerId, ct);
        if (usedByPlayer >= row.MaxPerPlayer) throw new ApiException(409, "GIFT_CODE_ALREADY_REDEEMED", "You have already redeemed this gift code the maximum number of times.");

        long? balanceAfter = null;
        switch (row.RewardKind)
        {
            case "coin":
            {
                var wallet = await db.Wallets.FromSqlInterpolated($"SELECT * FROM hero_chess.player_wallet WHERE player_id={playerId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "PLAYER_WALLET_NOT_FOUND", "The player wallet was not found.");
                wallet.Balance += row.RewardQuantity;
                balanceAfter = wallet.Balance;
                db.CoinTransactions.Add(new CoinTransaction
                {
                    Id = Guid.NewGuid(), PlayerId = playerId, IdempotencyKey = $"giftcode:{row.Id}:{usedByPlayer + 1}",
                    Kind = "admin_adjustment", Amount = row.RewardQuantity, BalanceAfter = wallet.Balance, CreatedAt = now
                });
                break;
            }
            case "hero":
            {
                var heroId = row.RewardHeroId!.Value;
                if (await db.PlayerHeroes.AsNoTracking().AnyAsync(x => x.PlayerId == playerId && x.HeroId == heroId, ct))
                    throw new ApiException(409, "HERO_ALREADY_OWNED", "You already own this hero.");
                db.PlayerHeroes.Add(new PlayerHero { PlayerId = playerId, HeroId = heroId, AcquiredVia = "admin", AcquiredAt = now });
                break;
            }
            case "cosmetic":
            {
                var cosmeticId = row.RewardCosmeticId!.Value;
                if (await db.PlayerCosmetics.AsNoTracking().AnyAsync(x => x.PlayerId == playerId && x.CosmeticId == cosmeticId, ct))
                    throw new ApiException(409, "COSMETIC_ALREADY_OWNED", "You already own this cosmetic.");
                db.PlayerCosmetics.Add(new PlayerCosmetic { PlayerId = playerId, CosmeticId = cosmeticId, AcquiredAt = now });
                break;
            }
        }

        db.GiftCodeRedemptions.Add(new GiftCodeRedemption
        {
            Id = Guid.NewGuid(), GiftCodeId = row.Id, PlayerId = playerId, RedemptionNo = usedByPlayer + 1,
            RewardKind = row.RewardKind, RewardQuantity = row.RewardQuantity, CoinBalanceAfter = balanceAfter, RedeemedAt = now
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(row.Code, row.RewardKind, row.RewardQuantity, row.RewardHeroId, row.RewardCosmeticId,
            (balanceAfter ?? 0).ToString());
    }

    // RedemptionCountsAsync: Đếm số lượt đã quy đổi theo gift code.
    private async Task<Dictionary<Guid, int>> RedemptionCountsAsync(IEnumerable<Guid> giftCodeIds, CancellationToken ct)
    {
        var ids = giftCodeIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var rows = await db.GiftCodeRedemptions.AsNoTracking().Where(x => ids.Contains(x.GiftCodeId)).GroupBy(x => x.GiftCodeId)
            .Select(g => new { GiftCodeId = g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(x => x.GiftCodeId, x => x.Count);
    }

    // ToDto: Dựng DTO gift code kèm số lượt đã quy đổi.
    private static AdminGiftCodeDto ToDto(GiftCode row, int redemptionCount) => new(row.Id, row.Code, row.RewardKind,
        row.RewardQuantity, row.RewardHeroId, row.RewardCosmeticId, row.StartsAt, row.ExpiresAt, row.MaxRedemptions,
        row.MaxPerPlayer, row.IsActive, redemptionCount, row.CreatedAt, row.UpdatedAt);

    // NewCode: Sinh mã gift code 12 ký tự dễ gõ, chỉ chữ hoa và số tránh ký tự nhầm lẫn.
    private static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(12, alphabet, (span, chars) =>
        {
            var random = Random.Shared;
            for (var i = 0; i < span.Length; i++) span[i] = chars[random.Next(chars.Length)];
        });
    }
}

// Vai trò file: Quản trị người chơi: tìm kiếm, ban/mở khoá, điều chỉnh ví, cấp/thu hero. Mọi thay đổi ghi audit.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class AdminPlayerService(AppDbContext db, AdminAuditWriter audit, TimeProvider clock)
{
    // ListAsync: Danh sách người chơi kèm Elo, số dư và số hero; tìm theo tên hoặc email.
    public async Task<AdminPlayerPageDto> ListAsync(string? query, string? status, string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var players = db.Players.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();
            players = players.Where(x => x.DisplayName.Contains(text) || (x.Email != null && x.Email.Contains(text)));
        }
        if (!string.IsNullOrWhiteSpace(status)) players = players.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var (at, id) = CursorCodec.Parse(cursor);
            players = players.Where(x => x.CreatedAt < at || (x.CreatedAt == at && x.Id.CompareTo(id) < 0));
        }
        var total = await players.CountAsync(ct);
        var rows = await players.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var dtos = await BuildPlayersAsync(page, ct);
        string? next = null;
        if (rows.Count > limit) next = CursorCodec.Format(page[^1].CreatedAt, page[^1].Id);
        return new(dtos, next, total);
    }

    // DetailAsync: Hồ sơ đầy đủ: hero đang sở hữu, đội hình, lịch sử kiểm duyệt và trận đấu.
    public async Task<AdminPlayerDetailDto> DetailAsync(Guid playerId, CancellationToken ct)
    {
        var player = await db.Players.AsNoTracking().SingleOrDefaultAsync(x => x.Id == playerId, ct)
            ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player was not found.");
        var dto = (await BuildPlayersAsync([player], ct))[0];
        var owned = await db.PlayerHeroes.AsNoTracking().Where(x => x.PlayerId == playerId).OrderBy(x => x.AcquiredAt)
            .Join(db.Heroes.AsNoTracking(), x => x.HeroId, h => h.Id, (ph, h) => new AdminOwnedHeroDto(h.Id, h.Code, h.Name, ph.AcquiredVia, ph.AcquiredAt))
            .ToListAsync(ct);
        var lineups = await db.Lineups.AsNoTracking().Where(x => x.PlayerId == playerId).OrderByDescending(x => x.UpdatedAt)
            .Select(x => new AdminLineupSummaryDto(x.Id, x.Name, x.Revision, x.Entries.Count, x.UpdatedAt)).ToListAsync(ct);
        var moderation = await db.AdminAuditLogs.AsNoTracking()
            .Where(x => x.EntityType == "player" && x.EntityId == playerId.ToString("D"))
            .OrderByDescending(x => x.CreatedAt).Take(50)
            .Select(x => new AdminModerationDto(x.Id, x.ActorUserId, x.Action, x.Reason, x.CreatedAt)).ToListAsync(ct);
        var matches = await db.MatchParticipants.AsNoTracking().Where(x => x.PlayerId == playerId)
            .Join(db.Matches.AsNoTracking(), x => x.MatchId, m => m.Id, (p, m) => new AdminPlayerMatchDto(m.Id, m.Mode, m.Status, m.Result, m.CreatedAt))
            .OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
        return new(dto, owned, lineups, moderation, matches);
    }

    // SetStatusAsync: Ban/mở khoá tài khoản; lý do bắt buộc và admin không tự khoá mình.
    public async Task<AdminPlayerDto> SetStatusAsync(Guid actorId, Guid playerId, AdminPlayerStatusRequest request, CancellationToken ct)
    {
        if (playerId == actorId) throw new ApiException(409, "CANNOT_MODIFY_SELF", "An administrator cannot change their own account status.");
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not ("active" or "disabled"))
            throw new ApiException(400, "INVALID_STATUS", "status must be 'active' or 'disabled'.");
        var reason = AdminValidation.RequireReason(request.Reason);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var player = await db.Users.FromSqlInterpolated($"SELECT * FROM hero_chess.user_account WHERE id={playerId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player was not found.");
        if (player.Role != "player") throw new ApiException(422, "NOT_A_PLAYER", "The account is not a player account.");
        var before = player.Status;
        player.Status = status;
        player.UpdatedAt = clock.GetUtcNow();
        audit.Write(actorId, status == "disabled" ? "player.ban" : "player.unban", "player", playerId.ToString("D"),
            new { status = before }, new { status }, reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await BuildPlayersAsync([(Player)player], ct))[0];
    }

    // AdjustCoinsAsync: Cộng/trừ coin thủ công; số dư không được âm và ghi ledger admin_adjustment.
    public async Task<AdminCoinAdjustmentDto> AdjustCoinsAsync(Guid actorId, Guid playerId, AdminCoinAdjustmentRequest request, CancellationToken ct)
    {
        var amount = AdminValidation.ParseCoinPrice(request.Amount);
        if (amount == 0) throw new ApiException(400, "INVALID_AMOUNT", "The amount must not be zero.");
        var reason = AdminValidation.RequireReason(request.Reason);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var wallet = await db.Wallets.FromSqlInterpolated($"SELECT * FROM hero_chess.player_wallet WHERE player_id={playerId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player wallet was not found.");
        var before = wallet.Balance;
        var after = before + amount;
        if (after < 0) throw new ApiException(422, "INSUFFICIENT_COINS", "The wallet does not have enough coins.", new { balance = before.ToString() });
        wallet.Balance = after;
        db.CoinTransactions.Add(new CoinTransaction
        {
            Id = Guid.NewGuid(), PlayerId = playerId, IdempotencyKey = $"admin-adjust:{Guid.NewGuid():N}",
            Kind = "admin_adjustment", Amount = amount, BalanceAfter = after, CreatedAt = clock.GetUtcNow()
        });
        audit.Write(actorId, "player.coins.adjust", "player", playerId.ToString("D"),
            new { balance = before.ToString() }, new { balance = after.ToString(), amount = amount.ToString() }, reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(playerId, before.ToString(), after.ToString(), amount.ToString());
    }

    // GrantHeroAsync: Cấp hero cho người chơi; nguồn 'admin' cho phép thu hồi về sau.
    public async Task<AdminOwnedHeroDto> GrantHeroAsync(Guid actorId, Guid playerId, AdminGrantHeroRequest request, CancellationToken ct)
    {
        var reason = AdminValidation.RequireReason(request.Reason);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hero = await db.Heroes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.HeroId, ct)
            ?? throw new ApiException(404, "HERO_NOT_FOUND", "The hero was not found.");
        var existing = await db.PlayerHeroes.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == playerId && x.HeroId == hero.Id, ct);
        if (existing is not null) throw new ApiException(409, "HERO_ALREADY_OWNED", "The player already owns this hero.");
        var now = clock.GetUtcNow();
        db.PlayerHeroes.Add(new PlayerHero { PlayerId = playerId, HeroId = hero.Id, AcquiredVia = "admin", AcquiredAt = now });
        audit.Write(actorId, "player.hero.grant", "player", playerId.ToString("D"), null, new { heroId = hero.Id.ToString("D"), heroCode = hero.Code }, reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(hero.Id, hero.Code, hero.Name, "admin", now);
    }

    // RevokeHeroAsync: Thu hero; chỉ thu được hero admin cấp, không đụng hero mua bằng coin.
    public async Task<AdminOwnedHeroDto> RevokeHeroAsync(Guid actorId, Guid playerId, Guid heroId, string? reason, CancellationToken ct)
    {
        var text = AdminValidation.RequireReason(reason);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var owned = await db.PlayerHeroes.FromSqlInterpolated($"SELECT * FROM hero_chess.player_hero WHERE player_id={playerId} AND hero_id={heroId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "HERO_NOT_OWNED", "The player does not own this hero.");
        if (owned.AcquiredVia != "admin")
            throw new ApiException(409, "HERO_NOT_ADMIN_GRANTED", "Only heroes granted by an administrator can be revoked.");
        var hero = await db.Heroes.AsNoTracking().SingleAsync(x => x.Id == heroId, ct);
        db.PlayerHeroes.Remove(owned);
        audit.Write(actorId, "player.hero.revoke", "player", playerId.ToString("D"),
            new { heroId = heroId.ToString("D"), acquiredVia = owned.AcquiredVia }, null, text);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(hero.Id, hero.Code, hero.Name, owned.AcquiredVia, owned.AcquiredAt);
    }

    // TransactionsAsync: Lịch sử biến động ví của một người chơi.
    public async Task<IReadOnlyList<AdminWalletTransactionDto>> TransactionsAsync(Guid playerId, CancellationToken ct)
    {
        return await db.CoinTransactions.AsNoTracking().Where(x => x.PlayerId == playerId).OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .Select(x => new AdminWalletTransactionDto(x.Id, x.Kind, x.Amount, x.BalanceAfter.ToString(), x.HeroId, x.MatchId, x.CreatedAt))
            .ToListAsync(ct);
    }

    // BuildPlayersAsync: Dựng DTO người chơi kèm Elo, số dư và số hero; tải theo lô.
    private async Task<IReadOnlyList<AdminPlayerDto>> BuildPlayersAsync(IReadOnlyList<Player> players, CancellationToken ct)
    {
        if (players.Count == 0) return [];
        var ids = players.Select(x => x.Id).ToArray();
        var ratings = await db.Ratings.AsNoTracking().Where(x => ids.Contains(x.PlayerId)).ToDictionaryAsync(x => x.PlayerId, ct);
        var wallets = await db.Wallets.AsNoTracking().Where(x => ids.Contains(x.PlayerId)).ToDictionaryAsync(x => x.PlayerId, ct);
        var heroCounts = await db.PlayerHeroes.AsNoTracking().Where(x => ids.Contains(x.PlayerId)).GroupBy(x => x.PlayerId)
            .Select(g => new { PlayerId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.PlayerId, x => x.Count, ct);
        return players.Select(player =>
        {
            var rating = ratings.GetValueOrDefault(player.Id);
            var wallet = wallets.GetValueOrDefault(player.Id);
            return new AdminPlayerDto(player.Id, player.DisplayName, player.Status, player.Email,
                rating?.Elo ?? 0, rating?.GamesPlayed ?? 0, rating?.Wins ?? 0, rating?.Draws ?? 0, rating?.Losses ?? 0,
                (wallet?.Balance ?? 0).ToString(), heroCounts.GetValueOrDefault(player.Id), player.CreatedAt);
        }).ToArray();
    }
}

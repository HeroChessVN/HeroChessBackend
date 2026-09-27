// Vai trò file: Hủy selection do participant yêu cầu hoặc worker phát hiện quá hạn; không dùng cho trận active.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class MatchSelectionLifetime(AppDbContext db, MatchLockRegistry locks, TimeProvider clock,
    SettlementService settlement, MatchConnectionHub hub)
{
    // CancelAsync: Khóa và kiểm tra quyền/deadline/status; chuyển cancelled, phát ended và settle không thưởng để giải phóng player.
    public async Task CancelAsync(Guid matchId, Guid? playerId, DateTimeOffset? expiredBefore, CancellationToken ct)
    {
        var gate = locks.For(matchId); await gate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var match = await db.Matches.FromSqlInterpolated($"SELECT * FROM hero_chess.game_match WHERE id={matchId} FOR UPDATE")
                .Include(x => x.Participants).SingleOrDefaultAsync(ct);
            if (match is null || (playerId is not null && !match.Participants.Any(x => x.PlayerId == playerId)))
                throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
            if (expiredBefore is not null && (match.Status != "selecting" || match.CreatedAt > expiredBefore)) return;
            if (match.Status == "cancelled") return;
            if (match.Status != "selecting") throw new ApiException(409, "MATCH_NOT_SELECTING", "Only a selecting match can be cancelled here.");
            match.Status = "cancelled"; match.EndReason = playerId is null ? "selection_timeout" : "selection_cancelled";
            match.EndedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            await hub.BroadcastAsync(matchId, new { type = "match.ended", payload = new { matchId, match.EndReason } }, CancellationToken.None);
            db.ChangeTracker.Clear();
            await settlement.SettleAsync(matchId, CancellationToken.None);
        }
        finally { gate.Release(); }
    }
}

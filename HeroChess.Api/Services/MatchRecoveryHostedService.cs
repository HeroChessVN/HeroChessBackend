// Vai trò file: Recovery chạy một lần lúc startup theo policy prototype: hủy trận dở, giữ lịch sử rồi settle.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Rules;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class MatchRecoveryHostedService(IServiceProvider services, TimeProvider clock, ILogger<MatchRecoveryHostedService> logger) : IHostedService
{
    // StartAsync: Hủy selecting/active còn lại vì server_restart; active append cancel snapshot; xử lý terminal chưa settled.
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unfinished = await db.Matches.AsNoTracking().Where(x => x.Status == "selecting" || x.Status == "active").Select(x => x.Id).ToListAsync(ct);
        foreach (var id in unfinished)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var match = await db.Matches.FromSqlInterpolated($"SELECT * FROM hero_chess.game_match WHERE id={id} FOR UPDATE").SingleAsync(ct);
            var now = clock.GetUtcNow();
            if (match.Status == "active")
            {
                var stateRow = await db.MatchStates.FromSqlInterpolated($"SELECT * FROM hero_chess.match_state WHERE match_id={id} FOR UPDATE").SingleAsync(ct);
                var state = GameJson.Read<GameState>(stateRow.State);
                // Phase 3.4: v3 → v4 state schema upgrade. Idempotent.
                // This ensures the cancelled state snapshot is v4 so the match can be resumed.
                state = StateSchemaUpgrade.UpgradeToCurrent(state);
                state.Version++; state.EndReason = "server_restart";
                stateRow.Version = state.Version; stateRow.State = GameJson.Document(state); stateRow.TurnDeadlineAt = null; stateRow.UpdatedAt = now;
                db.MatchActions.Add(new MatchAction { Id = Guid.NewGuid(), MatchId = id, SequenceNo = state.Version, CommandId = Guid.NewGuid(), Kind = "cancel",
                    RequestPayload = GameJson.Document(new { reason = "server_restart" }), ResolvedEvents = GameJson.Document(new[] { new { type = "match.cancelled", reason = "server_restart" } }),
                    StateAfter = GameJson.Document(state), StateSchemaVersion = state.StateSchemaVersion, ReceivedAt = now, CommittedAt = now });
            }
            match.Status = "cancelled"; match.EndReason = "server_restart"; match.EndedAt = now;
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            logger.LogWarning("Cancelled unfinished match {MatchId} during startup recovery.", id);
        }
        var terminal = await db.Matches.AsNoTracking().Where(x => (x.Status == "completed" || x.Status == "cancelled") && x.SettledAt == null).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in terminal)
        {
            db.ChangeTracker.Clear();
            await scope.ServiceProvider.GetRequiredService<SettlementService>().SettleAsync(id, ct);
        }
    }
    // StopAsync: Không có loop nền riêng cần dừng.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

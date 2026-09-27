// Vai trò file: Worker phục hồi công việc từ DB khi request/queue signal bị mất; chạy trong process đang hoạt động.
using HeroChess.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Services;

// Committed database state is the durable source of pending work; a lost request/queue signal is recoverable.
public sealed class MatchMaintenanceHostedService(IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<MatchRuntimeOptions> options, BotTurnScheduler bots, ILogger<MatchMaintenanceHostedService> logger) : BackgroundService
{
    // ExecuteAsync: Mỗi giây gọi sweep, tôn trọng shutdown token và log lỗi.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Match maintenance sweep failed."); }
        }
    }

    // SweepAsync: Hủy selecting quá hạn, settle terminal chưa settled dưới gate, rồi lên lịch lại bot đang tới lượt.
    public async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = clock.GetUtcNow().AddSeconds(-Math.Max(1, options.Value.SelectionSeconds));
        var work = await db.Matches.AsNoTracking().Where(x =>
            ((x.Status == "completed" || x.Status == "cancelled") && x.SettledAt == null) ||
            (x.Status == "selecting" && x.CreatedAt <= cutoff))
            .OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.Status }).ToListAsync(ct);
        foreach (var match in work)
        {
            try
            {
                await using var itemScope = scopes.CreateAsyncScope();
                if (match.Status == "selecting")
                    await itemScope.ServiceProvider.GetRequiredService<MatchSelectionLifetime>().CancelAsync(match.Id, null, cutoff, ct);
                else
                {
                    var gate = itemScope.ServiceProvider.GetRequiredService<MatchLockRegistry>().For(match.Id);
                    await gate.WaitAsync(ct);
                    try { await itemScope.ServiceProvider.GetRequiredService<SettlementService>().SettleAsync(match.Id, ct); }
                    finally { gate.Release(); }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) { logger.LogError(error, "Maintenance will retry match {MatchId}.", match.Id); }
        }
        var turns = await db.MatchStates.AsNoTracking().Where(x =>
            db.Matches.Any(m => m.Id == x.MatchId && m.Mode == "bot" && m.Status == "active") &&
            db.MatchParticipants.Any(p => p.MatchId == x.MatchId && p.ParticipantType == "bot" && p.Side == x.SideToMove))
            .Select(x => new { x.MatchId, x.Version }).ToListAsync(ct);
        foreach (var turn in turns) bots.Schedule(turn.MatchId, turn.Version);
    }
}

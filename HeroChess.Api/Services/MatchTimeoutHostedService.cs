// Vai trò file: Worker poll deadline; mọi timeout vẫn phải qua MatchCommandService để dùng chung lock/version.
using HeroChess.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Services;

public sealed class MatchTimeoutHostedService(IServiceScopeFactory scopes, TimeProvider clock, IOptions<MatchRuntimeOptions> options,
    ILogger<MatchTimeoutHostedService> logger) : BackgroundService
{
    // ExecuteAsync: Mỗi nhịp đọc tối đa 100 active state quá hạn, tạo scope riêng rồi gọi TimeoutAsync; log lỗi để sweep sau thử lại.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, options.Value.TimeoutPollMilliseconds)), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = clock.GetUtcNow();
                var due = await db.MatchStates.AsNoTracking().Where(x => x.TurnDeadlineAt != null && x.TurnDeadlineAt <= now &&
                    db.Matches.Any(m => m.Id == x.MatchId && m.Status == "active")).Select(x => new { x.MatchId, x.Version }).Take(100).ToListAsync(stoppingToken);
                foreach (var item in due)
                {
                    await using var commandScope = scopes.CreateAsyncScope();
                    await commandScope.ServiceProvider.GetRequiredService<MatchCommandService>().TimeoutAsync(item.MatchId, item.Version, now, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Match timeout sweep failed."); }
        }
    }
}

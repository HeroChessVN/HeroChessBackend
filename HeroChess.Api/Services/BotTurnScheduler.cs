// Vai trò file: Bot server cơ bản: chọn nước hợp lệ ưu tiên ăn SP, không phải AI có cấp Elo được hiệu chuẩn.
using System.Threading.Channels;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using HeroChess.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace HeroChess.Api.Services;

public sealed class BotTurnScheduler(IServiceScopeFactory scopes, ILogger<BotTurnScheduler> logger, IOptions<MatchRuntimeOptions> options) : BackgroundService
{
    private readonly Channel<(Guid MatchId, int Version)> _queue = Channel.CreateUnbounded<(Guid, int)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<(Guid, int), byte> _pending = new();
    // Schedule: Enqueue match/version, gộp các tín hiệu trùng còn pending.
    public void Schedule(Guid matchId, int version)
    {
        if (_pending.TryAdd((matchId, version), 0)) _queue.Writer.TryWrite((matchId, version));
    }

    // ExecuteAsync: Worker lấy DB snapshot mới, bỏ job stale/sai lượt, chọn legal move rồi gọi cùng command pipeline; không tự sửa DB bàn cờ.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == work.MatchId, stoppingToken);
                var row = await db.MatchStates.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == work.MatchId, stoppingToken);
                if (match?.Mode != "bot" || match.Status != "active" || row is null || row.Version != work.Version) continue;
                var botSide = await db.MatchParticipants.AsNoTracking().Where(x => x.MatchId == work.MatchId && x.ParticipantType == "bot").Select(x => x.Side).SingleAsync(stoppingToken);
                if (row.SideToMove != botSide) continue;
                var state = GameJson.Read<GameState>(row.State);
                var started = Stopwatch.GetTimestamp();
                var moves = new XiangqiRulesEngine().GenerateLegalActions(state);
                if (moves.Count == 0) continue;
                var chosen = moves.OrderByDescending(x => x.CapturedPieceId is null ? -1 : state.Pieces.Single(p => p.PieceId == x.CapturedPieceId).SetupPoints)
                    .ThenBy(x => x.PieceId).ThenBy(x => x.To.X).ThenBy(x => x.To.Y).First();
                if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(options.Value.BotThinkMilliseconds))
                { logger.LogWarning("Bot search exceeded its {Budget} ms budget for match {MatchId}.", options.Value.BotThinkMilliseconds, work.MatchId); continue; }
                var action = JsonSerializer.SerializeToElement(new { type = "move", pieceId = chosen.PieceId, to = new { x = chosen.To.X, y = chosen.To.Y } }, GameJson.Options);
                await scope.ServiceProvider.GetRequiredService<MatchCommandService>().ExecuteBotAsync(work.MatchId,
                    new MatchCommandRequest(Guid.NewGuid(), work.Version, action), DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (ApiException error) when (error.Code is "STALE_STATE" or "WRONG_TURN" or "MATCH_NOT_ACTIVE") { }
            catch (Exception error) { logger.LogError(error, "Bot turn failed for match {MatchId} version {Version}", work.MatchId, work.Version); }
            finally { _pending.TryRemove(work, out _); }
        }
    }
}

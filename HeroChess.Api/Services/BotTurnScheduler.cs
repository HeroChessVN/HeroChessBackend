// Vai trò file: Bot server cơ bản: chọn nước hợp lệ ưu tiên ăn SP, không phải AI có cấp Elo được hiệu chuẩn.
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace HeroChess.Api.Services;

public sealed class BotTurnScheduler(IServiceScopeFactory scopes, ILogger<BotTurnScheduler> logger, IOptions<MatchRuntimeOptions> options) : BackgroundService
{
    private readonly BotWorkQueue _queue = new(options.Value.BotQueueCapacity);
    // Schedule: Enqueue match/version, gộp các tín hiệu trùng còn pending.
    public void Schedule(Guid matchId, int version)
    {
        _queue.Schedule(matchId, version);
    }

    // ExecuteAsync: Worker lấy DB snapshot mới, bỏ job stale/sai lượt, chọn legal move rồi gọi cùng command pipeline; không tự sửa DB bàn cờ.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var work = await _queue.TakeAsync(stoppingToken);
                var outcome = "stale";
                try
                {
                    var readStarted = Stopwatch.GetTimestamp();
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == work.MatchId, stoppingToken);
                    var row = await db.MatchStates.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == work.MatchId, stoppingToken);
                    if (match?.Mode != "bot" || match.Status != "active" || row is null || row.Version != work.Version) continue;
                    var participant = await db.MatchParticipants.AsNoTracking().SingleAsync(x => x.MatchId == work.MatchId && x.ParticipantType == "bot", stoppingToken);
                    if (row.SideToMove != participant.Side) continue;
                    if (row.TurnDeadlineAt is not null && DateTimeOffset.UtcNow >= row.TurnDeadlineAt)
                    { outcome = "expired"; continue; }
                    BotTelemetry.StateRead.Record(Stopwatch.GetElapsedTime(readStarted).TotalSeconds);
                    var state = GameJson.Read<GameState>(row.State);
                    // Phase 3.4: v3 → v4 state schema upgrade. Idempotent.
                    state = StateSchemaUpgrade.UpgradeToCurrent(state);
                    var config = BotConfiguration.Read(participant.BotConfig);
                    var started = Stopwatch.GetTimestamp();
                    if (config != BotConfiguration.Legacy)
                    {
                        var allParticipants = await db.MatchParticipants.AsNoTracking().Where(x => x.MatchId == work.MatchId).ToListAsync(stoppingToken);
                        var lineups = allParticipants.ToDictionary(p => p.Side == "red" ? Side.Red : Side.Black, p => GameJson.Read<FrozenLineup>(p.LineupSnapshot));
                        var decision = new BotEngine(scope.ServiceProvider.GetRequiredService<CommandSkillDispatcher>()).Choose(work.MatchId,
                            state, config, GameJson.Read<RulesetSnapshot>(match.RulesetSnapshot), lineups, stoppingToken);
                        BotTelemetry.Search.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
                        outcome = decision.Action is null ? "no_action" : await Commit(scope.ServiceProvider, work, decision.Action.Value, stoppingToken);
                        logger.LogDebug("Bot decision match={MatchId} version={Version} difficulty={Difficulty} nodes={Nodes} depth={Depth} budgetExhausted={BudgetExhausted}",
                            work.MatchId, work.Version, config.Difficulty, decision.Nodes, decision.CompletedDepth, decision.BudgetExhausted);
                        continue;
                    }
                    var moves = new XiangqiRulesEngine().GenerateLegalActions(state);
                    if (moves.Count == 0)
                    {
                        var rescue = MatchCommandService.FindLegalSkillAction(state, new XiangqiRulesEngine(),
                            scope.ServiceProvider.GetRequiredService<CommandSkillDispatcher>());
                        BotTelemetry.Search.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
                        if (rescue is not null)
                            outcome = await Commit(scope.ServiceProvider, work, rescue.Value, stoppingToken);
                        else outcome = "no_action";
                        continue;
                    }
                    var chosen = moves.OrderByDescending(x => x.CapturedPieceId is null ? -1 : state.Pieces.Single(p => p.PieceId == x.CapturedPieceId).SetupPoints)
                        .ThenBy(x => x.PieceId).ThenBy(x => x.To.X).ThenBy(x => x.To.Y).First();
                    if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(options.Value.BotThinkMilliseconds))
                        logger.LogWarning("Bot search exceeded its {Budget} ms budget for match {MatchId}.", options.Value.BotThinkMilliseconds, work.MatchId);
                    var action = JsonSerializer.SerializeToElement(new { type = "move", pieceId = chosen.PieceId, to = new { x = chosen.To.X, y = chosen.To.Y } }, GameJson.Options);
                    BotTelemetry.Search.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
                    outcome = await Commit(scope.ServiceProvider, work, action, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { outcome = "cancelled"; }
                catch (ApiException error) when (error.Code == "TURN_DEADLINE_PASSED") { outcome = "expired"; }
                catch (ApiException error) when (error.Code is "STALE_STATE" or "WRONG_TURN" or "MATCH_NOT_ACTIVE") { }
                catch (InvalidDataException error)
                {
                    outcome = "failed";
                    logger.LogError(error, "Invalid bot configuration for match {MatchId}", work.MatchId);
                    try
                    {
                        await using var cancelScope = scopes.CreateAsyncScope();
                        await cancelScope.ServiceProvider.GetRequiredService<MatchCommandService>().CancelBotFailureAsync(work.MatchId, work.Version, stoppingToken);
                    }
                    catch (Exception cancelError) { logger.LogError(cancelError, "Bot failure cancellation will be retried for {MatchId}", work.MatchId); }
                }
                catch (Exception error) { outcome = "failed"; logger.LogError(error, "Bot turn failed for match {MatchId} version {Version}", work.MatchId, work.Version); }
                finally
                {
                    BotTelemetry.Turns.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
                    BotTelemetry.Total.Record(Stopwatch.GetElapsedTime(work.EnqueuedAt).TotalSeconds,
                        new KeyValuePair<string, object?>("outcome", outcome));
                    _queue.Complete(work);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _queue.Stop(); }
    }

    private static async Task<string> Commit(IServiceProvider services, BotWorkItem work, JsonElement action, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await services.GetRequiredService<MatchCommandService>().ExecuteBotAsync(work.MatchId,
                new MatchCommandRequest(Guid.NewGuid(), work.Version, action), DateTimeOffset.UtcNow, ct);
            return result.Duplicate ? "duplicate" : "success";
        }
        finally { BotTelemetry.Command.Record(Stopwatch.GetElapsedTime(started).TotalSeconds); }
    }
}

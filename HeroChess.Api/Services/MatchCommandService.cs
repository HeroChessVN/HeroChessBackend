// Vai trò file: Pipeline ghi trận dùng chung cho REST, WebSocket, bot và timeout; kiểm tra quyền/version rồi lưu state/action nguyên tử.
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Microsoft.EntityFrameworkCore;
using TurnLifecycle = HeroChess.Rules.TurnLifecycle;

namespace HeroChess.Api.Services;

public sealed class MatchCommandService(AppDbContext db, MatchLockRegistry locks, SettlementService settlement,
    MatchConnectionHub hub, TimeProvider clock, BotTurnScheduler bots, ILogger<MatchCommandService> logger,
    HeroChess.Rules.Skills.CommandSkillDispatcher dispatcher)
{
    // ExecuteAsync: Điểm vào cho player; ghi thời điểm server nhận command để xét deadline.
    public Task<MatchCommandResultDto> ExecuteAsync(Guid playerId, Guid matchId, MatchCommandRequest request, DateTimeOffset? receivedAt, CancellationToken ct) =>
        ExecuteActorAsync(playerId, false, matchId, request, receivedAt ?? clock.GetUtcNow(), ct);

    // ExecuteBotAsync: Điểm vào nội bộ cho participant bot; dùng cùng pipeline kiểm tra và ghi DB.
    public Task<MatchCommandResultDto> ExecuteBotAsync(Guid matchId, MatchCommandRequest request, DateTimeOffset? receivedAt, CancellationToken ct) =>
        ExecuteActorAsync(null, true, matchId, request, receivedAt ?? clock.GetUtcNow(), ct);

    // ExecuteActorAsync: Khóa trận, authorize, validate/dedup, apply move/resign/undo, commit; sau commit enqueue broadcast, lịch bot và settlement.
    private async Task<MatchCommandResultDto> ExecuteActorAsync(Guid? playerId, bool bot, Guid matchId, MatchCommandRequest request, DateTimeOffset received, CancellationToken ct)
    {
        var gate = locks.For(matchId); await gate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var participant = (bot
                ? await db.MatchParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId && x.ParticipantType == "bot", ct)
                : await db.MatchParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId && x.PlayerId == playerId, ct))
                ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
            ValidateAction(request.Action);
            var previous = await db.MatchActions.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId && x.CommandId == request.CommandId, ct);
            if (previous is not null)
            {
                if (previous.ActorSide != participant.Side || !JsonElement.DeepEquals(previous.RequestPayload.RootElement, request.Action))
                    throw new ApiException(409, "COMMAND_ID_CONFLICT", "The command id was already used with different content.");
                var currentMatch = await db.Matches.AsNoTracking().SingleAsync(x => x.Id == matchId, ct);
                var currentState = await db.MatchStates.AsNoTracking().SingleAsync(x => x.MatchId == matchId, ct);
                var historical = GameJson.Read<GameState>(previous.StateAfter);
                var previousState = new MatchState { MatchId = matchId, Version = previous.SequenceNo,
                    SideToMove = historical.SideToMove.ToString().ToLowerInvariant(), TurnIndex = historical.TurnIndex,
                    CountedActions = historical.CountedActions, TurnDeadlineAt = currentState.Version == previous.SequenceNo ? currentState.TurnDeadlineAt : null,
                    StateSchemaVersion = previous.StateSchemaVersion, State = GameJson.Document(historical), UpdatedAt = previous.CommittedAt };
                await transaction.CommitAsync(ct);
                var viewer = participant.Side == "red" ? Side.Red : Side.Black;
                return new(matchId, request.CommandId, previous.SequenceNo, true,
                    MatchStateProjection.Events(previous.ResolvedEvents.RootElement, viewer), MatchReadService.ToDto(currentMatch, previousState, clock.GetUtcNow(), viewer));
            }
            var match = await db.Matches.FromSqlInterpolated($"SELECT * FROM hero_chess.game_match WHERE id={matchId} FOR UPDATE").SingleAsync(ct);
            var row = await db.MatchStates.FromSqlInterpolated($"SELECT * FROM hero_chess.match_state WHERE match_id={matchId} FOR UPDATE").SingleOrDefaultAsync(ct)
                ?? throw new ApiException(409, "MATCH_NOT_STARTED", "The match has not started.");
            if (match.Status != "active") throw new ApiException(409, "MATCH_NOT_ACTIVE", "The match is not active.");
            if (request.ExpectedVersion != row.Version) throw new ApiException(409, "STALE_STATE", "The state has changed.", new { currentVersion = row.Version });
            if (row.TurnDeadlineAt is not null && received >= row.TurnDeadlineAt)
                throw new ApiException(409, "TURN_DEADLINE_PASSED", "The turn deadline has passed.", new { currentVersion = row.Version, deadlineAt = row.TurnDeadlineAt });

            var state = GameJson.Read<GameState>(row.State);
            // Phase 3.4: v3 → v4 state schema upgrade.
            // Upgrade is idempotent: already-v4 state returns immediately, no mutation.
            // Must happen before any TurnLifecycle call to ensure all v4 fields exist.
            state = StateSchemaUpgrade.UpgradeToCurrent(state);
            var actorSide = participant.Side == "red" ? Side.Red : Side.Black;

            // Phase 3.1 + 3.3: TurnLifecycle integration
            // Run lifecycle at the START of the authoritative player's turn (before they can act).
            // Exactly-once: use ProcessedTurns[(actorSide, state.TurnIndex)] as the key.
            // Lifecycle processes cooldowns, effect durations, stake lifetimes, pending steals,
            // and Creator Cancellation (internal Phản Kỳ resolution).
            //
            // REFACTORED: Apply() now executes core lifecycle (Steps 1-4) exactly once,
            // then applies Step 5 (Creator Cancellation) if resolveCreatorCancellation is true.
            // This eliminates the double-Apply pattern and ensures no double mutation.
            var lifecycleEvents = new List<object>();
            if (!TurnLifecycle.IsTurnProcessed(state, actorSide, state.TurnIndex))
            {
                // Single Apply() call handles all 5 steps:
                // 1. Cooldown decrement
                // 2. Effect duration decrement
                // 3. Stake lifetime decrement
                // 4. Pending steal finalization
                // 5. Creator Cancellation (resolveCreatorCancellation: true)
                var lifecycle = TurnLifecycle.Apply(state, actorSide, resolveCreatorCancellation: true);
                state = lifecycle.State;
                TurnLifecycle.MarkTurnProcessed(state, actorSide, state.TurnIndex);

                // Collect lifecycle events for broadcast
                lifecycleEvents.Add(new { type = "turn.started", side = actorSide.ToString().ToLowerInvariant() });
                foreach (var e in lifecycle.ExpiredEffects)
                    lifecycleEvents.Add(new { type = "effect.expired", effectId = e.EffectId, code = e.Code });
                foreach (var s in lifecycle.RemovedStakes)
                    lifecycleEvents.Add(new { type = "stake.removed", obstacleId = s.ObstacleId, position = new { x = s.Position.X, y = s.Position.Y } });
                foreach (var c in lifecycle.DecrementedCooldowns)
                    lifecycleEvents.Add(new { type = "skill.cooldown_decremented", side = c.Side.ToString().ToLowerInvariant(), slotNo = c.SlotNo, newCooldown = c.NewCooldown });
                foreach (var f in lifecycle.FinalizedSteals)
                    lifecycleEvents.Add(new { type = "phan_ky.finalized", effectId = f.EffectId, newController = f.NewController.ToString().ToLowerInvariant() });
                foreach (var r in lifecycle.RemovedEffects)
                    lifecycleEvents.Add(new { type = "phan_ky.cancelled", effectId = r.EffectId, removedBy = r.RemovedBy.ToString().ToLowerInvariant() });
            }

            var type = request.Action.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            GameState next;
            object[] events;
            switch (type)
            {
                case "move":
                    if (state.SideToMove != actorSide) throw new ApiException(422, "WRONG_TURN", "It is not this participant's turn.");
                    if (!request.Action.TryGetProperty("pieceId", out var piece) || !request.Action.TryGetProperty("to", out var to) ||
                        !to.TryGetProperty("x", out var x) || !to.TryGetProperty("y", out var y)) throw new ApiException(400, "INVALID_ACTION", "Move requires pieceId and to.x/to.y.");
                    var applied = new XiangqiRulesEngine().ApplyMove(state, new MoveAction(piece.GetGuid(), new BoardPoint(x.GetInt32(), y.GetInt32())));
                    if (!applied.Accepted) throw new ApiException(422, applied.Error!.Code, applied.Error.Message);
                    next = applied.State; events = new object[] { new { type = "piece.moved", pieceId = piece.GetGuid(), to = new { x = x.GetInt32(), y = y.GetInt32() } } };
                    break;
                case "resign":
                    next = state.Clone(); next.Version++; next.Result = actorSide == Side.Red ? "black_win" : "red_win"; next.EndReason = "resign";
                    events = new object[] { new { type = "match.resigned", side = participant.Side } }; break;
                case "undo":
                    if (match.Mode != "bot") throw new ApiException(422, "UNDO_NOT_ALLOWED", "Undo is available only in bot matches.");
                    if (!request.Action.TryGetProperty("targetSequence", out var targetValue)) throw new ApiException(400, "INVALID_ACTION", "Undo requires targetSequence.");
                    var target = targetValue.GetInt32();
                    if (target < 0 || target >= row.Version) throw new ApiException(422, "INVALID_UNDO_TARGET", "Undo must target an earlier snapshot.");
                    var action = await db.MatchActions.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId && x.SequenceNo == target, ct)
                        ?? throw new ApiException(422, "INVALID_UNDO_TARGET", "The target snapshot does not exist.");
                    next = GameJson.Read<GameState>(action.StateAfter);
                    // Phase 3.4: v3 → v4 upgrade for undo target state. Idempotent.
                    next = StateSchemaUpgrade.UpgradeToCurrent(next);
                    next.Version = row.Version + 1; next.Result = null; next.EndReason = null;
                    events = new object[] { new { type = "match.undone", targetSequence = target } }; break;
                case "hero_active":
                    if (state.SideToMove != actorSide) throw new ApiException(422, "WRONG_TURN", "It is not this participant's turn.");
                    if (!request.Action.TryGetProperty("pieceId", out var heroPieceId) || !heroPieceId.TryGetGuid(out var activePieceId) ||
                        !request.Action.TryGetProperty("skillCode", out var skillCode) ||
                        !request.Action.TryGetProperty("target", out var heroTarget))
                        throw new ApiException(400, "INVALID_ACTION", "hero_active requires pieceId, skillCode and target.");
                    if (skillCode.GetString() == SkillKeys.QuangTrungSpecialMove)
                    {
                        if (!heroTarget.TryGetProperty("to", out var destination) || destination.ValueKind != JsonValueKind.Object ||
                            !destination.TryGetProperty("x", out var heroX) || heroX.ValueKind != JsonValueKind.Number || !heroX.TryGetInt32(out var hx) ||
                            !destination.TryGetProperty("y", out var heroY) || heroY.ValueKind != JsonValueKind.Number || !heroY.TryGetInt32(out var hy) ||
                            hx is < 0 or > 8 || hy is < 0 or > 9)
                            throw new ApiException(400, "INVALID_ACTION", "Quang Trung skill requires target.to.x/to.y.");
                        var special = new XiangqiRulesEngine().ApplyHeroSpecialMove(state, new MoveAction(activePieceId, new BoardPoint(hx, hy)));
                        if (!special.Accepted) throw new ApiException(422, special.Error!.Code, special.Error.Message);
                        next = special.State;
                        events = new object[] { new { type = "hero_skill.activated", code = SkillKeys.QuangTrungSpecialMove, pieceId = activePieceId },
                            new { type = "piece.moved", pieceId = activePieceId, to = new { x = hx, y = hy } } };
                        break;
                    }
                    if (skillCode.GetString() != SkillKeys.ThDTuongCoc)
                        throw new ApiException(422, "HERO_SKILL_NOT_AVAILABLE", "The selected hero skill is not available.");
                    var heroResult = BachDangGiang.Execute(state, actorSide, activePieceId, heroTarget);
                    if (!heroResult.Accepted) throw new ApiException(422, heroResult.Error!.Code, heroResult.Error.Message);
                    next = heroResult.State;
                    if (new XiangqiRulesEngine().IsInCheck(next, actorSide))
                        throw new ApiException(422, "KING_IN_CHECK", "The skill must resolve check before ending the turn.");
                    next.Version++; next.TurnIndex++; next.CountedActions++;
                    next.ConsecutiveTimeouts[actorSide] = 0;
                    next.SideToMove = actorSide == Side.Red ? Side.Black : Side.Red;
                    events = heroResult.Events.ToArray();
                    break;
                case "team_skill":
                    // Extract slot from action (required field).
                    if (!request.Action.TryGetProperty("slot", out var slotProp) || !slotProp.TryGetInt32(out var slotNo))
                        throw new ApiException(400, "INVALID_ACTION", "team_skill requires a slot number.");
                    // Resolve the skill from the actor's current SkillStates.
                    var actorSkill = state.SkillStates.TryGetValue(actorSide, out var actorSkillList)
                        ? actorSkillList.FirstOrDefault(s => s.SlotNo == slotNo)
                        : null;
                    if (actorSkill == null)
                        throw new ApiException(422, "SKILL_NOT_IN_LINEUP", $"No skill found in slot {slotNo} for this lineup.");
                    var lineup = GameJson.Read<FrozenLineup>(participant.LineupSnapshot);
                    var lineupSkill = lineup.Skills.FirstOrDefault(s => s.SlotNo == slotNo && s.SkillId == actorSkill.SkillId)
                        ?? throw new ApiException(422, "SKILL_NOT_IN_LINEUP", "The skill is not in this match's lineup.");
                    var frozen = FrozenSkillSnapshotFactory.FromSkillState(actorSkill, lineupSkill.CooldownTurns);
                    var skillResult = dispatcher.Dispatch(state, actorSide, frozen, request.Action);
                    if (!skillResult.Accepted)
                        throw new ApiException(422, skillResult.Error!.Code, skillResult.Error.Message);
                    next = skillResult.State;
                    var engine = new XiangqiRulesEngine();
                    if (engine.IsInCheck(next, actorSide))
                        throw new ApiException(422, "KING_IN_CHECK", "The skill must resolve check before ending the turn.");
                    next.Version++;
                    next.TurnIndex++;
                    next.CountedActions++;
                    next.ConsecutiveTimeouts[actorSide] = 0;
                    next.SideToMove = actorSide == Side.Red ? Side.Black : Side.Red;
                    events = new object[]
                    {
                        new { type = "team_skill.activated", code = frozen.ImplementationKey, slot = slotNo, side = participant.Side }
                    }.Concat(skillResult.Events).ToArray();
                    break;
                case "start" or "timeout" or "cancel": throw new ApiException(400, "SERVER_ACTION_ONLY", "This action can only be created by the server.");
                default: throw new ApiException(400, "INVALID_ACTION", "Unknown match action.");
            }
            var nextTurnEvents = new List<object>();
            if (type is "move" or "team_skill" or "hero_active")
            {
                // Resolve mate after all turn-start effects and skills.
                if (next.Result is null && !TurnLifecycle.IsTurnProcessed(next, next.SideToMove, next.TurnIndex))
                {
                    var started = TurnLifecycle.Apply(next, next.SideToMove, resolveCreatorCancellation: true);
                    next = started.State;
                    TurnLifecycle.MarkTurnProcessed(next, next.SideToMove, next.TurnIndex);
                    nextTurnEvents.Add(new { type = "turn.started", side = next.SideToMove.ToString().ToLowerInvariant() });
                    foreach (var e in started.ExpiredEffects)
                        nextTurnEvents.Add(new { type = "effect.expired", effectId = e.EffectId, code = e.Code });
                    foreach (var e in started.RemovedStakes)
                        nextTurnEvents.Add(new { type = "stake.removed", obstacleId = e.ObstacleId, position = new { x = e.Position.X, y = e.Position.Y } });
                }
                FinishTurn(match, next);
            }
            // Phase 3.1: Merge lifecycle events with action events for broadcast.
            // Lifecycle events are prepended so they appear first in the event stream.
            events = lifecycleEvents.Concat(events).Concat(nextTurnEvents).ToArray();

            var now = clock.GetUtcNow();
            var rules = GameJson.Read<RulesetSnapshot>(match.RulesetSnapshot);
            row.Version = next.Version; row.SideToMove = next.SideToMove.ToString().ToLowerInvariant(); row.TurnIndex = next.TurnIndex;
            row.CountedActions = next.CountedActions; row.StateSchemaVersion = next.StateSchemaVersion; row.State = GameJson.Document(next);
            row.TurnDeadlineAt = next.Result is null ? now.AddSeconds(rules.TurnSeconds) : null; row.UpdatedAt = now;
            if (next.Result is not null) CompleteMatch(match, next, now);
            var resolved = GameJson.Document(events);
            var log = new MatchAction { Id = Guid.NewGuid(), MatchId = matchId, SequenceNo = next.Version, CommandId = request.CommandId,
                ActorSide = participant.Side, Kind = type!, RequestPayload = GameJson.Document(request.Action), ResolvedEvents = resolved,
                StateAfter = GameJson.Document(next), StateSchemaVersion = next.StateSchemaVersion, ReceivedAt = received, CommittedAt = now };
            db.MatchActions.Add(log);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            // The committed command must finish independently of the caller disconnecting.
            ct = CancellationToken.None;
            if (match.Status == "active") bots.Schedule(matchId, next.Version);
            var dto = new MatchCommandResultDto(matchId, request.CommandId, log.SequenceNo, false,
                MatchStateProjection.Events(resolved.RootElement, actorSide), MatchReadService.ToDto(match, row, clock.GetUtcNow(), actorSide));
            await hub.BroadcastAsync(matchId, new { type = "match.changed", payload = new { matchId, version = next.Version } }, ct);
            if (match.Status == "completed")
            {
                await hub.BroadcastAsync(matchId, new { type = "match.ended", payload = new { matchId, match.Result, match.EndReason } }, ct);
                await TrySettleAsync(matchId);
            }
            return dto;
        }
        finally { gate.Release(); }
    }

    // TimeoutAsync: Chỉ xử lý đúng version/deadline active; tăng lượt và chuỗi timeout, xử thua AFK/đang chiếu hoặc so SP khi đạt giới hạn.
    public async Task<bool> TimeoutAsync(Guid matchId, int expectedVersion, DateTimeOffset receivedAt, CancellationToken ct)
    {
        var gate = locks.For(matchId); await gate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var match = await db.Matches.FromSqlInterpolated($"SELECT * FROM hero_chess.game_match WHERE id={matchId} FOR UPDATE").SingleOrDefaultAsync(ct);
            var row = await db.MatchStates.FromSqlInterpolated($"SELECT * FROM hero_chess.match_state WHERE match_id={matchId} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (match?.Status != "active" || row is null || row.Version != expectedVersion || row.TurnDeadlineAt is null || receivedAt < row.TurnDeadlineAt) { await transaction.CommitAsync(ct); return false; }
            var state = GameJson.Read<GameState>(row.State); var engine = new XiangqiRulesEngine(); var now = clock.GetUtcNow();
            // Phase 3.4: v3 → v4 state schema upgrade. Idempotent.
            state = StateSchemaUpgrade.UpgradeToCurrent(state);
            var timedOut = state.SideToMove;
            var inCheck = engine.IsInCheck(state, timedOut);
            state.ConsecutiveTimeouts.TryGetValue(timedOut, out var streak);
            state.ConsecutiveTimeouts[timedOut] = ++streak;
            state.Version++; state.TurnIndex++; state.CountedActions++;
            var nextSide = timedOut == Side.Red ? Side.Black : Side.Red;
            state.SideToMove = nextSide;

            // Phase 3.2 + 3.3: Run TurnLifecycle for the new authoritative player.
            // This ensures cooldowns/effects/stakes are processed when a turn starts via timeout.
            // Exactly-once: use ProcessedTurns[(nextSide, state.TurnIndex)] as the key.
            //
            // REFACTORED: Apply() now executes core lifecycle (Steps 1-4) exactly once,
            // then applies Step 5 (Creator Cancellation) if resolveCreatorCancellation is true.
            var lifecycleEvents = new List<object>();
            if (!TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex))
            {
                // Single Apply() call handles all 5 steps:
                // 1. Cooldown decrement
                // 2. Effect duration decrement
                // 3. Stake lifetime decrement
                // 4. Pending steal finalization
                // 5. Creator Cancellation (resolveCreatorCancellation: true)
                var lifecycle = TurnLifecycle.Apply(state, nextSide, resolveCreatorCancellation: true);
                state = lifecycle.State;
                TurnLifecycle.MarkTurnProcessed(state, nextSide, state.TurnIndex);
                // Collect lifecycle events for broadcast
                lifecycleEvents.Add(new { type = "turn.started", side = nextSide.ToString().ToLowerInvariant() });
                foreach (var e in lifecycle.ExpiredEffects)
                    lifecycleEvents.Add(new { type = "effect.expired", effectId = e.EffectId, code = e.Code });
                foreach (var s in lifecycle.RemovedStakes)
                    lifecycleEvents.Add(new { type = "stake.removed", obstacleId = s.ObstacleId, position = new { x = s.Position.X, y = s.Position.Y } });
                foreach (var c in lifecycle.DecrementedCooldowns)
                    lifecycleEvents.Add(new { type = "skill.cooldown_decremented", side = c.Side.ToString().ToLowerInvariant(), slotNo = c.SlotNo, newCooldown = c.NewCooldown });
                foreach (var f in lifecycle.FinalizedSteals)
                    lifecycleEvents.Add(new { type = "phan_ky.finalized", effectId = f.EffectId, newController = f.NewController.ToString().ToLowerInvariant() });
                foreach (var r in lifecycle.RemovedEffects)
                    lifecycleEvents.Add(new { type = "phan_ky.cancelled", effectId = r.EffectId, removedBy = r.RemovedBy.ToString().ToLowerInvariant() });
            }

            if (inCheck || streak >= 2)
            {
                state.Result = timedOut == Side.Red ? "black_win" : "red_win";
                state.EndReason = inCheck ? "timeout_in_check" : "afk";
            }
            if (state.Result is null) FinishTurn(match, state);
            // A direct loss takes priority over SP comparison on action 150.
            ApplyActionLimit(match, state);
            var events = new List<object> { new { type = "turn.timeout", side = timedOut.ToString().ToLowerInvariant(), consecutiveTimeouts = streak } };
            // The next turn starts after the timeout action.
            if (lifecycleEvents.Count > 0)
                events.AddRange(lifecycleEvents);
            if (state.Result is not null) CompleteMatch(match, state, now);
            var rules = GameJson.Read<RulesetSnapshot>(match.RulesetSnapshot);
            row.Version = state.Version; row.SideToMove = state.SideToMove.ToString().ToLowerInvariant(); row.TurnIndex = state.TurnIndex; row.CountedActions = state.CountedActions;
            row.StateSchemaVersion = state.StateSchemaVersion; row.State = GameJson.Document(state); row.TurnDeadlineAt = match.Status == "active" ? now.AddSeconds(rules.TurnSeconds) : null; row.UpdatedAt = now;
            var resolved = GameJson.Document(events);
            db.MatchActions.Add(new MatchAction { Id = Guid.NewGuid(), MatchId = matchId, SequenceNo = state.Version, CommandId = Guid.NewGuid(), Kind = "timeout",
                RequestPayload = GameJson.Document(new { expectedVersion }), ResolvedEvents = resolved, StateAfter = GameJson.Document(state), StateSchemaVersion = state.StateSchemaVersion, ReceivedAt = receivedAt, CommittedAt = now });
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            ct = CancellationToken.None;
            if (match.Status == "active") bots.Schedule(matchId, state.Version);
            await hub.BroadcastAsync(matchId, new { type = "match.changed", payload = new { matchId, version = state.Version } }, ct);
            if (match.Status is "completed" or "cancelled")
            {
                await hub.BroadcastAsync(matchId, new { type = "match.ended", payload = new { matchId, match.Result, match.EndReason } }, ct);
                await TrySettleAsync(matchId);
            }
            return true;
        }
        finally { gate.Release(); }
    }

    // TrySettleAsync: Thử settle độc lập request cancellation; lỗi được log và worker sẽ retry từ DB.
    private async Task TrySettleAsync(Guid matchId)
    {
        try { db.ChangeTracker.Clear(); await settlement.SettleAsync(matchId, CancellationToken.None); }
        catch (Exception error) { logger.LogError(error, "Settlement deferred to recovery worker for match {MatchId}", matchId); }
    }

    // ValidateAction: Kiểm tra JSON shape, kiểu và range trước khi đọc action; input sai trả 400.
    private static void ValidateAction(JsonElement action)
    {
        // Invalid: Helper local tạo lỗi INVALID_ACTION 400 dùng chung khi JSON action sai shape/kiểu/range.
        static ApiException Invalid() => new(400, "INVALID_ACTION", "Action fields have an invalid shape or value.");
        if (action.ValueKind != JsonValueKind.Object || !action.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String) throw Invalid();
        if (kind.GetString() == "move")
        {
            if (!action.TryGetProperty("pieceId", out var piece) || piece.ValueKind != JsonValueKind.String || !piece.TryGetGuid(out _) ||
                !action.TryGetProperty("to", out var to) || to.ValueKind != JsonValueKind.Object ||
                !to.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Number || !x.TryGetInt32(out var px) || px is < 0 or > 8 ||
                !to.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.Number || !y.TryGetInt32(out var py) || py is < 0 or > 9) throw Invalid();
        }
        if (kind.GetString() == "undo" && (!action.TryGetProperty("targetSequence", out var target) ||
            target.ValueKind != JsonValueKind.Number || !target.TryGetInt32(out var sequence) || sequence < 0)) throw Invalid();
        if (kind.GetString() == "hero_active" &&
            (!action.TryGetProperty("pieceId", out var hero) || hero.ValueKind != JsonValueKind.String || !hero.TryGetGuid(out _) ||
             !action.TryGetProperty("skillCode", out var skill) || skill.ValueKind != JsonValueKind.String ||
             !action.TryGetProperty("target", out var skillTarget) || skillTarget.ValueKind != JsonValueKind.Object)) throw Invalid();
    }

    // ApplyActionLimit: Nếu chưa có kết quả trực tiếp và đạt giới hạn, so tổng SP frozen quân còn sống; bằng nhau hòa.
    private static void ApplyActionLimit(GameMatch match, GameState state)
    {
        var limit = GameJson.Read<RulesetSnapshot>(match.RulesetSnapshot).ActionLimit;
        if (state.Result is not null || state.CountedActions < limit) return;
        var red = state.Pieces.Where(x => x.Side == Side.Red && x.Status == PieceStatus.Alive).Sum(x => x.SetupPoints);
        var black = state.Pieces.Where(x => x.Side == Side.Black && x.Status == PieceStatus.Alive).Sum(x => x.SetupPoints);
        state.Result = red == black ? "draw" : red > black ? "red_win" : "black_win"; state.EndReason = "action_limit_sp";
    }
    private void FinishTurn(GameMatch match, GameState state)
    {
        if (state.Result is not null) return;
        var engine = new XiangqiRulesEngine();
        if (engine.GenerateLegalActions(state).Count == 0 && FindLegalSkillAction(state, engine, dispatcher) is null)
        {
            state.Result = state.SideToMove == Side.Red ? "black_win" : "red_win";
            state.EndReason = engine.IsInCheck(state, state.SideToMove) ? "checkmate" : "no_legal_actions";
        }
        ApplyActionLimit(match, state);
    }

    public static JsonElement? FindLegalSkillAction(GameState state, XiangqiRulesEngine engine, CommandSkillDispatcher dispatcher)
    {
        var side = state.SideToMove;
        foreach (var skill in state.SkillStates[side].Where(s => s.CooldownRemaining == 0 && s.UsesRemaining != 0))
        {
            IEnumerable<object> targets = skill.ImplementationKey switch
            {
                SkillKeys.Thanh or SkillKeys.Rao => Enumerable.Range(0, 9).SelectMany(x => Enumerable.Range(0, 10)
                    .Select(y => (object)new { position = new { x, y } })),
                SkillKeys.Khien => state.Pieces.Where(p => p.Side == side && p.Status == PieceStatus.Alive)
                    .Select(p => (object)new { pieceId = p.PieceId }),
                SkillKeys.VanCocTranGiang or SkillKeys.BinhLamThuyHien => new object[] { new { paths = new[] { 4, 5, 6 } } },
                SkillKeys.PhanKyDoatThe or SkillKeys.PhaTranDoatPhong => state.EffectInstances
                    .Select(e => (object)new { effectId = e.EffectId }),
                _ => Array.Empty<object>()
            };
            foreach (var target in targets)
            {
                var action = JsonSerializer.SerializeToElement(new { type = "team_skill", slot = skill.SlotNo, target }, GameJson.Options);
                var result = dispatcher.Dispatch(state, side, FrozenSkillSnapshotFactory.FromSkillState(skill), action);
                if (result.Accepted && !engine.IsInCheck(result.State, side)) return action;
            }
        }
        foreach (var hero in state.Pieces.Where(p => p.Side == side && p.Status == PieceStatus.Alive &&
            p.TraitKind == "active" && p.TraitImplementationKey == SkillKeys.ThDTuongCoc &&
            p.TraitState.GetValueOrDefault(SkillKeys.HeroCooldownRemainingKey) is not > 0))
        {
            for (var x = 0; x < 9; x++)
            for (var y = 4; y <= 5; y++)
            {
                var target = JsonSerializer.SerializeToElement(new { position = new { x, y } }, GameJson.Options);
                var result = BachDangGiang.Execute(state, side, hero.PieceId, target);
                if (result.Accepted && !engine.IsInCheck(result.State, side))
                    return JsonSerializer.SerializeToElement(new { type = "hero_active", pieceId = hero.PieceId,
                        skillCode = SkillKeys.ThDTuongCoc, target = new { position = new { x, y } } }, GameJson.Options);
            }
        }
        foreach (var hero in state.Pieces.Where(p => p.Side == side && p.Status == PieceStatus.Alive &&
            p.TraitImplementationKey is SkillKeys.QuangTrungSpecialMove or "general.orthogonal_range_3"))
        {
            var move = engine.GenerateQuangTrungSpecialMoves(state, hero.PieceId).FirstOrDefault();
            if (move is not null)
                return JsonSerializer.SerializeToElement(new { type = "hero_active", pieceId = hero.PieceId,
                    skillCode = SkillKeys.QuangTrungSpecialMove, target = new { to = new { x = move.To.X, y = move.To.Y } } }, GameJson.Options);
        }
        return null;
    }
    // CompleteMatch: Đồng bộ trạng thái completed, result, endReason và endedAt từ GameState vào entity trận.
    private static void CompleteMatch(GameMatch match, GameState state, DateTimeOffset now)
    { match.Status = "completed"; match.Result = state.Result; match.EndReason = state.EndReason; match.EndedAt = now; }
}

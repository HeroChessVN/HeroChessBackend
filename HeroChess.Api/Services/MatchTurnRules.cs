using System.Text.Json;
using HeroChess.Rules;
using HeroChess.Rules.Skills;

namespace HeroChess.Api.Services;

// Pure transitions shared by authoritative commands and bot simulation. No DB, clocks or broadcasting.
public static class MatchTurnRules
{
    public static GameState Prepare(GameState state)
    {
        if (TurnLifecycle.IsTurnProcessed(state, state.SideToMove, state.TurnIndex)) return state;
        var result = TurnLifecycle.Apply(state, state.SideToMove, resolveCreatorCancellation: true).State;
        TurnLifecycle.MarkTurnProcessed(result, result.SideToMove, result.TurnIndex);
        return result;
    }

    public static CommandSkillResult ApplySkill(GameState state, Side actor, FrozenSkillSnapshot skill,
        JsonElement action, CommandSkillDispatcher dispatcher)
    {
        var result = dispatcher.Dispatch(state, actor, skill, action);
        if (!result.Accepted) return result;
        if (new XiangqiRulesEngine().IsInCheck(result.State, actor))
            return CommandSkillResult.Failure(state, "KING_IN_CHECK", "The skill must resolve check before ending the turn.");
        var next = result.State;
        next.Version++; next.TurnIndex++; next.CountedActions++;
        next.ConsecutiveTimeouts[actor] = 0;
        next.SideToMove = actor == Side.Red ? Side.Black : Side.Red;
        return CommandSkillResult.Success(next, result.Events);
    }

    public static (GameState State, object[] Events, IReadOnlyList<LegalMove> Replies) AfterAction(GameState next, RulesetSnapshot rules, CommandSkillDispatcher dispatcher)
    {
        var events = new List<object>();
        if (next.Result is null && !TurnLifecycle.IsTurnProcessed(next, next.SideToMove, next.TurnIndex))
        {
            var started = TurnLifecycle.Apply(next, next.SideToMove, resolveCreatorCancellation: true);
            next = started.State;
            TurnLifecycle.MarkTurnProcessed(next, next.SideToMove, next.TurnIndex);
            events.Add(new { type = "turn.started", side = next.SideToMove.ToString().ToLowerInvariant() });
            foreach (var e in started.ExpiredEffects)
                events.Add(new { type = "effect.expired", effectId = e.EffectId, code = e.Code });
            foreach (var e in started.RemovedStakes)
                events.Add(new { type = "stake.removed", obstacleId = e.ObstacleId, position = new { x = e.Position.X, y = e.Position.Y } });
        }
        var replies = Finish(next, rules, dispatcher);
        return (next, events.ToArray(), replies);
    }

    public static IReadOnlyList<LegalMove> Finish(GameState state, RulesetSnapshot rules, CommandSkillDispatcher dispatcher)
    {
        if (state.Result is not null) return [];
        var engine = new XiangqiRulesEngine();
        // Never use the bot's bounded/top-K candidates to decide whether a match has ended.
        var replies = engine.GenerateLegalActions(state);
        if (replies.Count == 0 && MatchCommandService.FindLegalSkillAction(state, engine, dispatcher) is null)
        {
            state.Result = state.SideToMove == Side.Red ? "black_win" : "red_win";
            state.EndReason = engine.IsInCheck(state, state.SideToMove) ? "checkmate" : "no_legal_actions";
        }
        ApplyActionLimit(state, rules.ActionLimit);
        return state.Result is null ? replies : [];
    }

    public static void ApplyActionLimit(GameState state, int limit)
    {
        if (state.Result is not null || state.CountedActions < limit) return;
        var red = state.Pieces.Where(p => p.Side == Side.Red && p.Status == PieceStatus.Alive).Sum(p => p.SetupPoints);
        var black = state.Pieces.Where(p => p.Side == Side.Black && p.Status == PieceStatus.Alive).Sum(p => p.SetupPoints);
        state.Result = red == black ? "draw" : red > black ? "red_win" : "black_win";
        state.EndReason = "action_limit_sp";
    }
}

using System.Text.Json;
using HeroChess.Api.Infrastructure;
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
        AdvanceSkill(result.State, actor);
        return result;
    }

    public static CommandSkillResult ApplyHeroSkill(GameState state, JsonElement action, XiangqiRulesEngine engine)
    {
        var actor = state.SideToMove;
        var pieceId = action.GetProperty("pieceId").GetGuid();
        var key = action.GetProperty("skillCode").GetString();
        var target = action.GetProperty("target");
        var piece = state.Pieces.FirstOrDefault(p => p.PieceId == pieceId && p.Side == actor &&
            p.Status == PieceStatus.Alive && p.TraitImplementationKey == key);
        if (!state.PublicRiverStakes || state.Result is not null || piece is null || key is not (SkillKeys.QuangTrungSpecialMove or SkillKeys.ThDTuongCoc))
            return CommandSkillResult.Failure(state, "HERO_SKILL_NOT_AVAILABLE", "The selected hero skill is not available.");

        var activated = new { type = "hero_skill.activated", code = key, pieceId, side = actor,
            skillName = piece.TraitName ?? (key == SkillKeys.ThDTuongCoc ? "Bạch Đằng Giang" : "Hành Quân Thần Tốc"),
            heroName = piece.HeroName, pieceClass = piece.Class };
        if (key == SkillKeys.QuangTrungSpecialMove)
        {
            if (!target.TryGetProperty("to", out var to) || to.ValueKind != JsonValueKind.Object ||
                !to.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Number || !x.TryGetInt32(out var px) ||
                !to.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.Number || !y.TryGetInt32(out var py) ||
                !new BoardPoint(px, py).IsOnBoard)
                return CommandSkillResult.Failure(state, "INVALID_TARGET", "Quang Trung requires target.to.x/to.y.");
            var move = new MoveAction(pieceId, new BoardPoint(px, py));
            var applied = engine.ApplyHeroSpecialMove(state, move);
            return applied.Accepted ? CommandSkillResult.Success(applied.State, new[] { (object)activated, MoveEvent(state, move, applied) }) :
                CommandSkillResult.Failure(state, applied.Error!.Code, applied.Error.Message);
        }

        var placed = BachDangGiang.Execute(state, actor, pieceId, target);
        if (!placed.Accepted) return placed;
        if (engine.IsInCheck(placed.State, actor))
            return CommandSkillResult.Failure(state, "KING_IN_CHECK", "The skill must resolve check before ending the turn.");
        AdvanceSkill(placed.State, actor);
        return CommandSkillResult.Success(placed.State, new object[] { activated });
    }

    public static object MoveEvent(GameState before, MoveAction move, ApplyMoveResult applied)
    {
        if (applied.StakeHit is not { } hit) return new { type = "piece.moved", pieceId = move.PieceId, to = move.To };
        var piece = before.Pieces.Single(p => p.PieceId == move.PieceId);
        return new { type = "river_stake.hit", pieceId = move.PieceId, position = hit.Position, obstacleId = hit.ObstacleId,
            side = before.ObstacleMetadata[hit.ObstacleId].Placer, victimSide = piece.Side, victimClass = piece.Class, victimName = piece.HeroName };
    }

    public static IEnumerable<JsonElement> HeroCandidates(GameState state, XiangqiRulesEngine engine)
    {
        if (!state.PublicRiverStakes || state.Result is not null) yield break;
        foreach (var hero in state.Pieces.Where(p => p.Side == state.SideToMove && p.Status == PieceStatus.Alive && p.Position is not null))
        {
            if (hero.TraitImplementationKey == SkillKeys.QuangTrungSpecialMove)
            {
                foreach (var move in engine.GenerateQuangTrungSpecialMoves(state, hero.PieceId))
                    yield return Make(hero, new { to = move.To });
            }
            else if (hero.TraitImplementationKey == SkillKeys.ThDTuongCoc &&
                hero.TraitState.GetValueOrDefault(SkillKeys.HeroCooldownRemainingKey) is not > 0 &&
                !state.ThdTuongCocStakes.ContainsKey(hero.PieceId))
            {
                foreach (var x in new[] { 4, 3, 5, 2, 6, 1, 7, 0, 8 })
                foreach (var y in new[] { 4, 5 })
                {
                    var point = new BoardPoint(x, y);
                    if (!state.Pieces.Any(p => p.Status == PieceStatus.Alive && p.Position == point) && !state.Obstacles.Any(o => o.Position == point))
                        yield return Make(hero, new { position = point });
                }
            }
        }

        static JsonElement Make(PieceState hero, object target) =>
            GameJson.Element(new { type = "hero_active", pieceId = hero.PieceId, skillCode = hero.TraitImplementationKey, target });
    }

    private static void AdvanceSkill(GameState next, Side actor)
    {
        next.Version++; next.TurnIndex++; next.CountedActions++;
        next.ConsecutiveTimeouts[actor] = 0;
        next.SideToMove = actor == Side.Red ? Side.Black : Side.Red;
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

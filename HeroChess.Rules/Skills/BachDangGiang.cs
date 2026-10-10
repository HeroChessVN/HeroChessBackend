using System.Text.Json;

namespace HeroChess.Rules.Skills;

public static class BachDangGiang
{
    public static CommandSkillResult Execute(GameState state, Side actor, Guid pieceId, JsonElement target)
    {
        var hero = state.Pieces.FirstOrDefault(p => p.PieceId == pieceId && p.Side == actor &&
            p.Status == PieceStatus.Alive && p.Position is not null && p.Class == PieceClass.Elephant &&
            p.TraitKind == "active" && p.TraitImplementationKey == SkillKeys.ThDTuongCoc);
        if (hero is null)
            return CommandSkillResult.Failure(state, "HERO_SKILL_NOT_AVAILABLE", "The selected hero cannot use Bạch Đằng Giang.");
        if (hero.TraitState.GetValueOrDefault(SkillKeys.HeroCooldownRemainingKey) is > 0)
            return CommandSkillResult.Failure(state, "SKILL_ON_COOLDOWN", "Bạch Đằng Giang is on cooldown.");
        if (target.ValueKind != JsonValueKind.Object || !target.TryGetProperty("position", out var point) ||
            point.ValueKind != JsonValueKind.Object ||
            !point.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Number || !x.TryGetInt32(out var px) ||
            !point.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.Number || !y.TryGetInt32(out var py) ||
            px is < 0 or > 8 || py is not (4 or 5))
            return CommandSkillResult.Failure(state, "INVALID_TARGET", "Choose an empty river cell (y=4 or y=5).");
        var position = new BoardPoint(px, py);
        if (state.Pieces.Any(p => p.Status == PieceStatus.Alive && p.Position == position) ||
            state.Obstacles.Any(o => o.Position == position && o.Kind != SkillKeys.ObstacleKindThDTuongCoc))
            return CommandSkillResult.Failure(state, "CELL_OCCUPIED", "Choose an empty river cell.");

        var next = state.Clone();
        var stakeId = Guid.NewGuid();
        next.Obstacles.Add(new ObstacleState(stakeId, position, SkillKeys.ObstacleKindThDTuongCoc, 3));
        next.ObstacleMetadata[stakeId] = new ObstacleMetadata { Placer = actor };
        next.ThdTuongCocStakes[pieceId] = stakeId;
        next.Pieces.Single(p => p.PieceId == pieceId).TraitState[SkillKeys.HeroCooldownRemainingKey] = SkillKeys.ThDTuongCocCooldownTurns;
        return CommandSkillResult.Success(next, new object[] { new { type = "hero_skill.activated", code = SkillKeys.ThDTuongCoc, pieceId } });
    }
}

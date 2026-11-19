using System.Text.Json;

namespace HeroChess.Rules.Skills;

public static class AdvisorHeroSkills
{
    public static bool IsActive(string? key) => key is SkillKeys.LeLaiSwapGeneral or SkillKeys.TranThuDoGeneralShield;

    public static bool TryReadParameters(string? key, JsonElement? parameters, out int cooldown, out int duration)
    {
        cooldown = duration = 0;
        if (!IsActive(key) || parameters is not { ValueKind: JsonValueKind.Object } obj ||
            !Positive(obj, "cooldownTurns", out cooldown)) return false;
        return key == SkillKeys.LeLaiSwapGeneral ||
            Positive(obj, "durationTurns", out duration) && duration >= 2 && cooldown > duration;

        static bool Positive(JsonElement obj, string key, out int value)
        {
            value = 0;
            return obj.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number &&
                field.TryGetInt32(out value) && value > 0;
        }
    }

    public static CommandSkillResult Execute(GameState state, Side actor, Guid pieceId, string key, JsonElement target)
    {
        if (state.SideToMove != actor) return Fail("WRONG_TURN", "It is not this side's turn.");
        var hero = state.Pieces.FirstOrDefault(p => p.PieceId == pieceId && p.Side == actor && p.Class == PieceClass.Advisor &&
            p.Status == PieceStatus.Alive && p.Position is not null && p.TraitKind == "active" && p.TraitImplementationKey == key);
        if (!state.PublicRiverStakes || state.Result is not null || !IsActive(key) || hero is null)
            return Fail("HERO_SKILL_NOT_AVAILABLE", "The selected advisor skill is not available.");
        if (hero.TraitState.GetValueOrDefault(SkillKeys.HeroCooldownRemainingKey) is > 0)
            return Fail("SKILL_ON_COOLDOWN", "The advisor skill is on cooldown.");
        if (!TryReadParameters(key, hero.TraitParameters, out var cooldown, out var duration))
            return Fail("INVALID_HERO_SKILL_CONFIG", "The advisor skill requires a positive cooldown and a shorter shield duration of at least two shared turns.");
        if (target.ValueKind != JsonValueKind.Object || !target.TryGetProperty("pieceId", out var value) ||
            value.ValueKind != JsonValueKind.String || !value.TryGetGuid(out var generalId))
            return Fail("INVALID_TARGET", "Select a living allied General.");
        var general = state.Pieces.FirstOrDefault(p => p.PieceId == generalId && p.Side == actor && p.Class == PieceClass.General &&
            p.Status == PieceStatus.Alive && p.Position is not null);
        if (general is null) return Fail("INVALID_TARGET", "Select a living allied General.");
        if (key == SkillKeys.LeLaiSwapGeneral)
        {
            if (!InPalace(actor, hero.Position!.Value) || !InPalace(actor, general.Position!.Value) || hero.Position == general.Position)
                return Fail("INVALID_TARGET", "Both pieces must occupy distinct cells in their own palace.");
            if (Rooted(hero) || Rooted(general)) return Fail("PIECE_ROOTED", "A rooted piece cannot swap positions.");
        }
        else if (general.Effects.Any(e => e.Code == SkillKeys.ShieldEffect && e.RemainingTurns > 0))
            return Fail("ALREADY_SHIELDED", "The General already has a shield.");

        var next = state.Clone();
        var source = next.Pieces.Single(p => p.PieceId == pieceId);
        var receiver = next.Pieces.Single(p => p.PieceId == generalId);
        source.TraitState[SkillKeys.HeroCooldownRemainingKey] = cooldown;
        if (key == SkillKeys.LeLaiSwapGeneral)
        {
            (source.Position, receiver.Position) = (receiver.Position, source.Position);
            return CommandSkillResult.Success(next, new object[]
            {
                new { type = "piece.moved", pieceId, from = hero.Position, to = source.Position },
                new { type = "piece.moved", pieceId = generalId, from = general.Position, to = receiver.Position }
            });
        }
        receiver.Effects.Add(new EffectState(SkillKeys.ShieldEffect, pieceId, duration));
        return CommandSkillResult.Success(next, new object[] { new { type = "shield.applied", pieceId = generalId } });

        CommandSkillResult Fail(string code, string message) => CommandSkillResult.Failure(state, code, message);
        static bool Rooted(PieceState piece) => piece.Effects.Any(e => e.Code == FactionSkillHandler.Rooted && e.RemainingTurns > 0);
        static bool InPalace(Side side, BoardPoint point) => point.X is >= 3 and <= 5 &&
            (side == Side.Red ? point.Y is >= 0 and <= 2 : point.Y is >= 7 and <= 9);
    }
}

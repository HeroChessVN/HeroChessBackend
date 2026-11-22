using System.Text.Json;

namespace HeroChess.Rules.Skills;

public static class SkillTiming
{
    public static int Version(JsonElement config) => config.ValueKind == JsonValueKind.Object &&
        config.TryGetProperty("skillTimingVersion", out var value)
        ? value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var version) && version is 1 or 2 ? version : 0
        : 1;

    public static bool Tick(GameState state, int? startedAt) =>
        state.SkillTimingVersion < 2 || startedAt != state.TurnIndex - 1;

    public static bool Positive(JsonElement? parameters, string name, out int value)
    {
        value = 0;
        return parameters is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(name, out var field) &&
            field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out value) && value > 0;
    }

    public static bool FitsTurn(int turn, int duration) => duration > 0 && turn >= 0 && duration < int.MaxValue - turn;

    public static bool ValidCommand(string key, int cooldown, JsonElement? parameters) => FitsTurn(0, cooldown) &&
        (key is not (SkillKeys.Thanh or SkillKeys.Rao or SkillKeys.Khien or FactionSkillHandler.Trap or FactionSkillHandler.Rally) ||
         Positive(parameters, "durationTurns", out var duration) && FitsTurn(0, duration)) &&
        (key != FactionSkillHandler.Trap || Positive(parameters, "rootDurationTurns", out var root) && FitsTurn(0, root));

    public static bool ValidHero(string key, JsonElement? parameters)
    {
        if (key is SkillKeys.QuangTrungSpecialMove or SkillKeys.ThDTuongCoc)
            return Positive(parameters, "cooldownTurns", out var cooldown) && FitsTurn(0, cooldown) &&
                (key != SkillKeys.ThDTuongCoc || Positive(parameters, "durationTurns", out var duration) && FitsTurn(0, duration));
        return !AdvisorHeroSkills.IsActive(key) || AdvisorHeroSkills.TryReadParameters(key, parameters, out var cd, out var lifetime) &&
            FitsTurn(0, cd) && (lifetime == 0 || FitsTurn(0, lifetime));
    }

    public static int Duration(CommandSkillContext ctx, int legacyDuration) => ctx.State.SkillTimingVersion < 2
        ? legacyDuration : Positive(ctx.Parameters, "durationTurns", out var duration) ? duration : 0;
}

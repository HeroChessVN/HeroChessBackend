using System.Text.Json;
using HeroChess.Api.Infrastructure;
using HeroChess.Rules;
using HeroChess.Rules.Skills;

namespace HeroChess.Api.Services;

public static class MatchStateProjection
{
    public static JsonElement State(JsonElement stored, Side viewer)
    {
        var state = stored.Deserialize<GameState>(GameJson.Options) ?? throw new InvalidOperationException("Invalid stored state.");
        var hidden = state.Obstacles.Where(o => o.Kind == SkillKeys.ObstacleKindThDTuongCoc &&
            (!state.ObstacleMetadata.TryGetValue(o.ObstacleId, out var metadata) || metadata.Placer != viewer))
            .Select(o => o.ObstacleId).ToHashSet();
        state.Obstacles.RemoveAll(o => hidden.Contains(o.ObstacleId));
        foreach (var id in hidden)
        {
            state.ObstacleMetadata.Remove(id);
            state.StakeMetadata.Remove(id);
        }
        foreach (var id in state.ThdTuongCocStakes.Where(x => hidden.Contains(x.Value)).Select(x => x.Key).ToArray())
            state.ThdTuongCocStakes.Remove(id);
        using var projected = GameJson.Document(state);
        return projected.RootElement.Clone();
    }

    public static JsonElement Events(JsonElement stored, Side viewer)
    {
        if (stored.ValueKind != JsonValueKind.Array) return stored.Clone();
        var safe = stored.EnumerateArray().Select(e =>
        {
            if (e.TryGetProperty("type", out var type) && type.GetString() == "stake.removed")
                return JsonSerializer.SerializeToElement(new { type = "stake.removed" }, GameJson.Options);
            if (e.TryGetProperty("kind", out var kind) && kind.GetString() == SkillKeys.ObstacleKindThDTuongCoc &&
                e.TryGetProperty("creator", out var creator) &&
                !string.Equals(creator.GetString(), viewer.ToString(), StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.SerializeToElement(new { type = "hero_skill.activated", code = SkillKeys.ThDTuongCoc }, GameJson.Options);
            return e.Clone();
        }).ToArray();
        return JsonSerializer.SerializeToElement(safe, GameJson.Options);
    }
}

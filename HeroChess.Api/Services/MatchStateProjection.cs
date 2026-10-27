using System.Text.Json;
using System.Text.Json.Nodes;
using HeroChess.Api.Infrastructure;
using HeroChess.Rules;
using HeroChess.Rules.Skills;

namespace HeroChess.Api.Services;

public static class MatchStateProjection
{
    public static JsonElement State(JsonElement stored, Side viewer, IReadOnlyDictionary<Guid, string>? heroNames = null)
    {
        var state = stored.Deserialize<GameState>(GameJson.Options) ?? throw new InvalidOperationException("Invalid stored state.");
        var hidden = state.Obstacles.Where(o => o.Kind == SkillKeys.ObstacleKindThDTuongCoc)
            .Select(o => o.ObstacleId).Concat(state.ThdTuongCocStakes.Values).ToHashSet();
        state.Obstacles.RemoveAll(o => hidden.Contains(o.ObstacleId));
        foreach (var id in hidden)
        {
            state.ObstacleMetadata.Remove(id);
            state.StakeMetadata.Remove(id);
        }
        state.ThdTuongCocStakes.Clear();
        var projected = JsonSerializer.SerializeToNode(state, GameJson.Options)!.AsObject();
        foreach (var piece in projected["pieces"]!.AsArray())
        {
            var data = piece!.AsObject();
            foreach (var key in new[] { "movementImplementationKey", "traitKind", "traitImplementationKey", "traitName", "traitDescription", "traitParameters", "traitState" })
                data.Remove(key);
            // Quân cũ lưu state trước khi có heroName: bù tên tướng từ catalog để bàn cờ hiện đủ cho cả hai phe.
            if (heroNames is not null && (data["heroName"] is null || data["heroName"]!.GetValueKind() == JsonValueKind.Null))
            {
                var heroId = data["heroId"]?.GetValue<Guid>();
                if (heroId is not null && heroNames.TryGetValue(heroId.Value, out var name)) data["heroName"] = name;
            }
        }
        return GameJson.Element(projected);
    }

    public static JsonElement Events(JsonElement stored, Side viewer)
    {
        if (stored.ValueKind != JsonValueKind.Array) return stored.Clone();
        var safe = stored.EnumerateArray().Where(e =>
            !(e.TryGetProperty("type", out var type) && type.GetString() == "hero_skill.activated") &&
            !(e.TryGetProperty("kind", out var kind) && kind.GetString() == SkillKeys.ObstacleKindThDTuongCoc))
            .Select(e =>
        {
            if (e.TryGetProperty("type", out var type) && type.GetString() == "stake.removed")
                return JsonSerializer.SerializeToElement(new { type = "stake.removed" }, GameJson.Options);
            return e.Clone();
        }).ToArray();
        return JsonSerializer.SerializeToElement(safe, GameJson.Options);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;

namespace HeroChess.Api.Services;

// Immutable profiles: changing strength/budgets requires a new version, never relabel an old match.
public sealed record BotConfiguration(int SchemaVersion, string? Difficulty, string StrategyVersion,
    string SearchProfileVersion, string RulesEngineVersion)
{
    public const string RecaptureStrategy = "tactical-v2";
    public static readonly BotConfiguration Legacy = new(0, null, "capture_sp_v1", "legacy", "rules-v1");
    [JsonIgnore] public (int Depth, int Width, int Nodes, int Milliseconds) Limits => Difficulty switch
    {
        "easy" => (0, 16, 32, 50), "medium" => (1, 12, 64, 100), "hard" => (2, 8, 256, 200),
        _ => throw new InvalidDataException("Unsupported bot profile.")
    };

    public static BotConfiguration ForRequest(string mode, string? difficulty)
    {
        if (difficulty is null) return Legacy; // old clients keep their existing bot
        var normalized = difficulty.Trim().ToLowerInvariant();
        if (mode != "bot" || normalized is not ("easy" or "medium" or "hard"))
            throw new ApiException(400, "INVALID_BOT_DIFFICULTY", "Difficulty must be easy, medium or hard, for bot mode only.");
        return new(1, normalized, normalized == "hard" ? RecaptureStrategy : "tactical-v1", "bounded-v1", "rules-v1");
    }

    public static BotConfiguration Read(JsonDocument? document)
    {
        try
        {
            if (document is null) throw new InvalidDataException("Missing bot configuration.");
            var json = document.RootElement;
            if (json.TryGetProperty("strategy", out var old) && old.ValueKind == JsonValueKind.String && old.GetString() == Legacy.StrategyVersion &&
                !json.TryGetProperty("schemaVersion", out _)) return Legacy;
            var config = json.Deserialize<BotConfiguration>(GameJson.Options);
            if (config == Legacy) return Legacy;
            if (config?.Difficulty is null)
                throw new InvalidDataException("Unsupported bot configuration version.");
            var current = ForRequest("bot", config.Difficulty);
            if (config != current && config != (current with { StrategyVersion = "tactical-v1" }))
                throw new InvalidDataException("Unsupported bot configuration version.");
            return config;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ApiException)
        { throw new InvalidDataException("Invalid bot configuration.", error); }
    }

    public static BotInfoDto? Client(JsonDocument? document)
    {
        if (document is null) return null;
        try
        {
            var config = Read(document);
            return config.Difficulty is null ? null : new(config.Difficulty, config.StrategyVersion, config.SearchProfileVersion);
        }
        catch (InvalidDataException) { return null; } // history stays readable; worker handles corruption
    }
}

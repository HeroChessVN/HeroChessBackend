using System.Text.Json;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class ForecastAlgorithmTests
{
    [Fact]
    public void Forecast_avoids_a_poisoned_cannon_capture_and_keeps_the_original_board_unchanged()
    {
        var state = Position();
        var original = GameJson.Element(state);
        var cannon = state.Pieces.Single(p => p.Class == PieceClass.Cannon);
        var horse = state.Pieces.Single(p => p.Class == PieceClass.Horse);
        var engine = BotDifficultyTests.Engine();
        var forecast = GameJson.Element(engine.Forecast(state, BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default));
        Assert.Equal(5, forecast.GetProperty("variation").GetArrayLength());
        var first = forecast.GetProperty("variation")[0].GetProperty("action");
        Assert.Equal("black", forecast.GetProperty("initialBoard").GetProperty("sideToMove").GetString());
        var next = engine.Simulate(state, first, BotDifficultyTests.Rules(), BotDifficultyTests.Lineups())!;
        Assert.Equal(PieceStatus.Alive, next.Pieces.Single(p => p.PieceId == horse.PieceId).Status);
        Assert.DoesNotContain(new XiangqiRulesEngine().GenerateLegalActions(next), m => m.CapturedPieceId == cannon.PieceId);
        Assert.True(JsonElement.DeepEquals(original, GameJson.Element(state)));

        foreach (var entry in forecast.GetProperty("variation").EnumerateArray())
        {
            Assert.Equal(state.SideToMove == Side.Red ? "red" : "black", entry.GetProperty("side").GetString());
            state = engine.Simulate(state, entry.GetProperty("action"), BotDifficultyTests.Rules(), BotDifficultyTests.Lineups())!;
            Assert.NotNull(state);
            Assert.True(JsonElement.DeepEquals(MatchStateProjection.State(GameJson.Element(state), Side.Red), entry.GetProperty("boardAfter")));
        }
        var threats = forecast.GetProperty("threats").EnumerateArray().Select(x => x.GetProperty("action").GetRawText()).ToArray();
        Assert.Equal(threats.Length, threats.Distinct().Count());
        Assert.InRange(threats.Length, 0, 2);
    }

    [Fact]
    public void Unseen_opponent_snares_cannot_change_the_forecast_or_mutate_the_input()
    {
        var state = Position();
        var baseline = GameJson.Element(BotDifficultyTests.Engine().Forecast(state,
            BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default, Side.Red));
        state.RiverSnares.Add(new(Side.Black, new(1, 5), 20));
        state.RiverSnares.Add(new(Side.Black, new(0, 4), 20));
        var hidden = GameJson.Element(BotDifficultyTests.Engine().Forecast(state,
            BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default, Side.Red));
        Assert.True(JsonElement.DeepEquals(baseline, hidden));
        Assert.Equal(2, state.RiverSnares.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Forecast_stops_when_the_match_ends_instead_of_padding_the_five_action_sequence(int remaining)
    {
        var state = Position();
        state.CountedActions = 150 - remaining;
        var forecast = GameJson.Element(BotDifficultyTests.Engine().Forecast(state,
            BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default));
        var entries = forecast.GetProperty("variation").EnumerateArray().ToArray();
        Assert.Equal(remaining, entries.Length);
        var action = entries[^1];
        Assert.NotEqual(JsonValueKind.Null, action.GetProperty("boardAfter").GetProperty("result").ValueKind);
        Assert.Equal(150 - remaining, state.CountedActions);
        Assert.Null(state.Result);
    }

    [Fact]
    public void Soft_cutoff_still_completes_five_valid_actions_and_cancellation_is_observed()
    {
        var state = Position();
        var engine = BotDifficultyTests.Engine(new ElapsedClock(300));
        var forecast = GameJson.Element(engine.Forecast(state, BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default));
        AssertComplete(engine, state, forecast, BotDifficultyTests.Lineups());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => engine.Forecast(state,
            BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), cancellation.Token));
    }

    [Theory]
    [InlineData(Side.Red)]
    [InlineData(Side.Black)]
    public void Invalid_optional_skill_candidates_do_not_spend_the_completion_budget(Side side)
    {
        var state = Position(); state.SideToMove = side;
        foreach (var actor in new[] { Side.Red, Side.Black })
            state.SkillStates[actor].AddRange(new[] { new SkillState(1, Guid.NewGuid(), 1, 0, SkillKeys.Khien),
                new SkillState(2, Guid.NewGuid(), 1, 0, SkillKeys.Thanh), new SkillState(3, Guid.NewGuid(), 1, 0, SkillKeys.Rao) });
        var engine = BotDifficultyTests.Engine();
        var forecast = GameJson.Element(engine.Forecast(state, BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default));
        AssertComplete(engine, state, forecast, BotDifficultyTests.Lineups());
    }

    [Theory]
    [InlineData(SkillKeys.Khien)]
    [InlineData(FactionSkillHandler.Forecast)]
    public void Skill_only_turn_is_a_real_ply_and_does_not_recurse_into_forecast(string key)
    {
        var state = new GameState { PublicRiverStakes = true, SideToMove = Side.Black,
            Pieces = [BotDifficultyTests.Piece(PieceClass.General, Side.Red, 3, 0, 100),
                BotDifficultyTests.Piece(PieceClass.General, Side.Black, 5, 9, 100)] };
        state.Pieces[1].Effects.Add(new EffectState(FactionSkillHandler.Rooted, null, 3));
        var id = Guid.NewGuid(); state.SkillStates[Side.Black].Add(new(1, id, 1, 0, key));
        var lineups = BotDifficultyTests.Lineups();
        lineups[Side.Black] = lineups[Side.Black] with { Skills = [new(1, id, key, 1, 15)] };
        var engine = new BotEngine(new CommandSkillDispatcher(new CommandSkillRegistry([
            new KhienHandler(), new FactionSkillHandler(FactionSkillHandler.Forecast)])), new ElapsedClock(300));
        var forecast = GameJson.Element(engine.Forecast(state, BotDifficultyTests.Rules(), lineups, default));
        Assert.Equal("team_skill", forecast.GetProperty("variation")[0].GetProperty("action").GetProperty("type").GetString());
        AssertComplete(engine, state, forecast, lineups);
    }

    [Fact]
    public void A_terminal_initial_board_needs_no_padding_and_hard_timeout_never_returns_partial_success()
    {
        var state = Position(); state.Result = "draw";
        var forecast = GameJson.Element(BotDifficultyTests.Engine().Forecast(state, BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default));
        Assert.Empty(forecast.GetProperty("variation").EnumerateArray());
        Assert.Equal("draw", forecast.GetProperty("initialBoard").GetProperty("result").GetString());
        state.Result = null; var original = GameJson.Element(state);
        Assert.Throws<TimeoutException>(() => BotDifficultyTests.Engine(new ElapsedClock(2500))
            .Forecast(state, BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default));
        Assert.True(JsonElement.DeepEquals(original, GameJson.Element(state)));
    }

    private static void AssertComplete(BotEngine engine, GameState state, JsonElement forecast, Dictionary<Side, FrozenLineup> lineups)
    {
        var original = GameJson.Element(state); var cursor = state.Clone();
        var entries = forecast.GetProperty("variation").EnumerateArray().ToArray();
        Assert.Equal(5, entries.Length);
        foreach (var entry in entries)
        {
            Assert.Equal(cursor.SideToMove == Side.Red ? "red" : "black", entry.GetProperty("side").GetString());
            cursor = engine.Simulate(cursor, entry.GetProperty("action"), BotDifficultyTests.Rules(), lineups)!;
            Assert.NotNull(cursor);
            Assert.True(JsonElement.DeepEquals(MatchStateProjection.State(GameJson.Element(cursor), Side.Red), entry.GetProperty("boardAfter")));
        }
        Assert.True(JsonElement.DeepEquals(original, GameJson.Element(state)));
    }

    private static GameState Position() => new()
    {
        PublicRiverStakes = true, SideToMove = Side.Black,
        Pieces = [BotDifficultyTests.Piece(PieceClass.General, Side.Red, 3, 0, 100),
            BotDifficultyTests.Piece(PieceClass.General, Side.Black, 5, 9, 100),
            BotDifficultyTests.Piece(PieceClass.Cannon, Side.Black, 1, 7, 6),
            BotDifficultyTests.Piece(PieceClass.Horse, Side.Red, 1, 0, 4),
            BotDifficultyTests.Piece(PieceClass.Soldier, Side.Red, 1, 3, 1),
            BotDifficultyTests.Piece(PieceClass.Rook, Side.Red, 0, 0, 7)]
    };

    private sealed class ElapsedClock(long elapsed) : TimeProvider
    {
        private long _time;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Increment(ref _time) == 1 ? 0 : elapsed;
    }
}

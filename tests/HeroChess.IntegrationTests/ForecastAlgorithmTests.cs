using System.Text.Json;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Rules;
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
        var first = forecast.GetProperty("variation")[0].GetProperty("action");
        var next = engine.Simulate(state, first, BotDifficultyTests.Rules(), BotDifficultyTests.Lineups())!;
        Assert.Equal(PieceStatus.Alive, next.Pieces.Single(p => p.PieceId == horse.PieceId).Status);
        Assert.DoesNotContain(new XiangqiRulesEngine().GenerateLegalActions(next), m => m.CapturedPieceId == cannon.PieceId);
        Assert.True(JsonElement.DeepEquals(original, GameJson.Element(state)));

        foreach (var entry in forecast.GetProperty("variation").EnumerateArray())
        {
            Assert.Equal(state.SideToMove == Side.Red ? "red" : "black", entry.GetProperty("side").GetString());
            state = engine.Simulate(state, entry.GetProperty("action"), BotDifficultyTests.Rules(), BotDifficultyTests.Lineups())!;
            Assert.NotNull(state);
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

    [Fact]
    public void Cutoff_and_cancellation_never_publish_unchecked_actions()
    {
        var state = Position();
        var engine = BotDifficultyTests.Engine(new ExpiredClock());
        var forecast = GameJson.Element(engine.Forecast(state, BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default));
        Assert.Empty(forecast.GetProperty("variation").EnumerateArray());
        Assert.Empty(forecast.GetProperty("threats").EnumerateArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => engine.Forecast(state,
            BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), cancellation.Token));
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

    private sealed class ExpiredClock : TimeProvider
    {
        private long _time;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Add(ref _time, 100);
    }
}

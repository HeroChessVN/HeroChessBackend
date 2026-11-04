using System.Text.Json;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class BotDifficultyTests
{
    [Theory]
    [InlineData(" easy ", "easy")]
    [InlineData("MEDIUM", "medium")]
    [InlineData("hard", "hard")]
    public void Difficulty_is_normalized_and_frozen(string input, string expected)
    {
        var config = BotConfiguration.ForRequest("bot", input);
        Assert.Equal(expected, config.Difficulty);
        Assert.Equal(config, BotConfiguration.Read(GameJson.Document(config)));
    }

    [Fact]
    public void Legacy_and_invalid_config_are_not_silently_relabelled()
    {
        Assert.Null(BotConfiguration.ForRequest("bot", null).Difficulty);
        var hard = BotConfiguration.ForRequest("bot", "hard");
        Assert.Equal("tactical-v2", hard.StrategyVersion);
        var previousHard = hard with { StrategyVersion = "tactical-v1" };
        Assert.Equal(previousHard, BotConfiguration.Read(GameJson.Document(previousHard)));
        Assert.Throws<ApiException>(() => BotConfiguration.ForRequest("ranked", "easy"));
        Assert.Throws<ApiException>(() => BotConfiguration.ForRequest("bot", ""));
        Assert.Throws<InvalidDataException>(() => BotConfiguration.Read(GameJson.Document(new { difficulty = "hard", schemaVersion = 999 })));
    }

    [Theory]
    [InlineData("easy")]
    [InlineData("medium")]
    [InlineData("hard")]
    public void Search_returns_legal_action_without_mutating_root(string difficulty)
    {
        var state = Position();
        var before = GameJson.Document(state).RootElement.GetRawText();
        var engine = Engine();
        var result = engine.Choose(Guid.Empty, state, BotConfiguration.ForRequest("bot", difficulty), Rules(), Lineups(), CancellationToken.None);
        Assert.NotNull(result.Action);
        var after = engine.Simulate(state, result.Action!.Value, Rules(), Lineups());
        Assert.NotNull(after);
        Assert.Equal(Side.Black, after!.SideToMove);
        Assert.Equal(before, GameJson.Document(state).RootElement.GetRawText());
    }

    [Fact]
    public void Hard_avoids_the_poisoned_capture_that_medium_takes()
    {
        var state = Position();
        var engine = Engine();
        var medium = engine.Choose(Guid.Empty, state, BotConfiguration.ForRequest("bot", "medium"), Rules(), Lineups(), CancellationToken.None);
        var hard = engine.Choose(Guid.Empty, state, BotConfiguration.ForRequest("bot", "hard"), Rules(), Lineups(), CancellationToken.None);
        Assert.NotNull(medium.Action);
        Assert.NotNull(hard.Action);
        var mediumState = engine.Simulate(state, medium.Action!.Value, Rules(), Lineups())!;
        var hardState = engine.Simulate(state, hard.Action!.Value, Rules(), Lineups())!;
        Assert.Equal(PieceStatus.Captured, mediumState.Pieces.Single(p => p.PieceId == state.Pieces[3].PieceId).Status);
        Assert.Equal(PieceStatus.Alive, hardState.Pieces.Single(p => p.PieceId == state.Pieces[3].PieceId).Status);
    }

    [Theory]
    [InlineData(2)] // budget gone before the regular root search
    [InlineData(11)] // budget gone before checking the opponent's replies
    [InlineData(12)] // budget gone during the opponent's replies
    public void Hard_does_not_trade_a_cannon_for_a_protected_horse_on_cutoff(int cutoff)
    {
        var state = new GameState { SideToMove = Side.Black };
        var cannon = Piece(PieceClass.Cannon, Side.Black, 1, 7, 6);
        var horse = Piece(PieceClass.Horse, Side.Red, 1, 0, 4);
        state.Pieces.AddRange([Piece(PieceClass.General, Side.Red, 3, 0, 100),
            Piece(PieceClass.General, Side.Black, 5, 9, 100), cannon, horse,
            Piece(PieceClass.Soldier, Side.Red, 1, 3, 1), Piece(PieceClass.Rook, Side.Red, 0, 0, 7)]);
        var engine = Engine(new CutoffClock(cutoff));
        var choice = engine.Choose(Guid.Empty, state, BotConfiguration.ForRequest("bot", "hard"), Rules(), Lineups(), CancellationToken.None);
        Assert.True(choice.BudgetExhausted);
        var after = engine.Simulate(state, choice.Action!.Value, Rules(), Lineups())!;
        Assert.Equal(PieceStatus.Alive, after.Pieces.Single(p => p.PieceId == horse.PieceId).Status);
        Assert.DoesNotContain(new XiangqiRulesEngine().GenerateLegalActions(after), move => move.CapturedPieceId == cannon.PieceId);
    }

    [Fact]
    public void Skill_rescue_uses_frozen_cooldown_and_is_available_when_no_move_exists()
    {
        var state = new GameState { SideToMove = Side.Black };
        state.Pieces.AddRange(new[] { Piece(PieceClass.General, Side.Red, 3, 0, 100), Piece(PieceClass.General, Side.Black, 4, 9, 100),
            Piece(PieceClass.Soldier, Side.Red, 4, 8, 1), Piece(PieceClass.Rook, Side.Red, 3, 8, 10),
            Piece(PieceClass.Rook, Side.Red, 5, 8, 10), Piece(PieceClass.Rook, Side.Red, 4, 6, 10) });
        var id = Guid.NewGuid();
        state.SkillStates[Side.Black].Add(new(1, id, 1, 0, SkillKeys.Khien));
        var lineups = Lineups();
        lineups[Side.Black] = lineups[Side.Black] with { Skills = [new(1, id, SkillKeys.Khien, 1, 7)] };
        var engine = Engine();
        var choice = engine.Choose(Guid.Empty, state, BotConfiguration.ForRequest("bot", "hard"), Rules(), lineups, CancellationToken.None);
        Assert.Equal("team_skill", choice.Action!.Value.GetProperty("type").GetString());
        var next = engine.Simulate(state, choice.Action.Value, Rules(), lineups)!;
        Assert.Equal(6, next.SkillStates[Side.Black][0].CooldownRemaining);
        Assert.Equal(0, next.SkillStates[Side.Black][0].UsesRemaining);
        Assert.Equal(Side.Red, next.SideToMove);
    }

    [Fact]
    public void Cancellation_is_observed_before_search_and_hidden_legacy_state_is_rejected()
    {
        var engine = Engine();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => engine.Choose(Guid.Empty, Position(), BotConfiguration.ForRequest("bot", "hard"), Rules(), Lineups(), cancelled.Token));
        var state = Position();
        state.ThdTuongCocStakes.Add(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<InvalidDataException>(() => engine.Choose(Guid.Empty, state, BotConfiguration.ForRequest("bot", "easy"), Rules(), Lineups(), CancellationToken.None));
    }

    [Fact]
    public void Action_limit_in_simulation_matches_authoritative_rule()
    {
        var state = Position();
        state.CountedActions = 149;
        var choice = Engine().Choose(Guid.Empty, state, BotConfiguration.ForRequest("bot", "medium"), Rules(), Lineups(), CancellationToken.None);
        var next = Engine().Simulate(state, choice.Action!.Value, Rules(), Lineups())!;
        Assert.Equal("action_limit_sp", next.EndReason);
        Assert.NotNull(next.Result);
        Assert.Null(state.Result);
    }

    [Fact]
    public void Structures_in_early_slots_do_not_starve_later_ready_skills()
    {
        var state = Position();
        state.SkillStates[Side.Red].AddRange(new[] {
            new SkillState(1, Guid.NewGuid(), 1, 0, SkillKeys.Thanh),
            new SkillState(2, Guid.NewGuid(), 1, 0, SkillKeys.Rao),
            new SkillState(3, Guid.NewGuid(), 1, 0, SkillKeys.Khien) });
        var slots = BotEngine.SkillCandidates(state).Take(8).Select(a => a.GetProperty("slot").GetInt32()).Distinct().Order().ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, slots);
    }

    internal static BotEngine Engine(TimeProvider? clock = null) => new(new CommandSkillDispatcher(new CommandSkillRegistry(new ICommandSkillHandler[]
        { new ThanhHandler(), new RaoHandler(), new KhienHandler(), new VanCocTranGiangHandler(), new BinhLamThuyHienHandler(), new PhanKyDoatTheHandler(), new PhaTranDoatPhongHandler() })), clock ?? new FrozenClock());
    internal static RulesetSnapshot Rules() => new("test", 100, 90, 150, JsonSerializer.SerializeToElement(new { }), "test", "test", 32, 0, 0);
    internal static Dictionary<Side, FrozenLineup> Lineups() => new()
    {
        [Side.Red] = new(Guid.Empty, 1, 0, [], []), [Side.Black] = new(Guid.Empty, 1, 0, [], [])
    };
    private static GameState Position()
    {
        var state = new GameState();
        state.Pieces.AddRange(new[]
        {
            Piece(PieceClass.General, Side.Red, 3, 0, 100), Piece(PieceClass.General, Side.Black, 5, 9, 100),
            Piece(PieceClass.Rook, Side.Red, 0, 4, 10), Piece(PieceClass.Soldier, Side.Black, 0, 6, 1),
            Piece(PieceClass.Rook, Side.Black, 1, 6, 10)
        });
        return state;
    }
    internal static PieceState Piece(PieceClass type, Side side, int x, int y, int sp) => new()
        { PieceId = Guid.NewGuid(), Class = type, Side = side, Position = new(x, y), StartPosition = new(x, y), SetupPoints = sp };
    private sealed class FrozenClock : TimeProvider { public override long GetTimestamp() => 0; }
    private sealed class CutoffClock(int cutoff) : TimeProvider
    {
        private int _calls;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ++_calls >= cutoff ? 250 : 0;
    }
}

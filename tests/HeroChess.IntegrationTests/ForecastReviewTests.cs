using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class ForecastReviewTests
{
    private readonly CommandSkillDispatcher _dispatcher = new(new CommandSkillRegistry([new FactionSkillHandler(FactionSkillHandler.Forecast)]));

    [Fact]
    public void Review_spends_the_skill_once_but_advances_the_turn_only_when_confirmed()
    {
        var state = Position();
        var original = GameJson.Element(state);
        var skill = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0], 16);
        var action = GameJson.Element(new { type = "team_skill", slot = 1, target = new { } });
        var cast = MatchTurnRules.ApplySkill(state, Side.Red, skill, action, _dispatcher, reviewForecast: true);
        Assert.True(cast.Accepted);
        Assert.True(System.Text.Json.JsonElement.DeepEquals(original, GameJson.Element(state)));
        var pending = GameJson.Read<GameState>(GameJson.Document(cast.State));
        Assert.Equal(1, pending.Version);
        Assert.Equal(new ForecastReviewState(Side.Red, 1), pending.ForecastReview);
        Assert.Equal(0, pending.CountedActions);
        Assert.Equal(0, pending.TurnIndex);
        Assert.Equal(Side.Red, pending.SideToMove);
        Assert.Equal(16, pending.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Equal(0, pending.SkillStates[Side.Red][0].UsesRemaining);
        Assert.False(MatchTurnRules.ApplySkill(pending, Side.Red, skill, action, _dispatcher, true).Accepted);
        Assert.False(MatchTurnRules.ApplyHeroSkill(pending, GameJson.Element(new { }), new()).Accepted);
        var engine = new XiangqiRulesEngine();
        Assert.Empty(engine.GenerateLegalActions(pending));
        Assert.Equal("TURN_ALREADY_COMMITTED", engine.ApplyMove(pending, new(pending.Pieces[0].PieceId, new(3, 1))).Error!.Code);
        Assert.Empty(MatchTurnRules.AfterAction(pending, BotDifficultyTests.Rules(), _dispatcher).Replies);
        var done = MatchTurnRules.AfterAction(MatchTurnRules.CompleteForecastReview(pending), BotDifficultyTests.Rules(), _dispatcher).State;
        Assert.Null(done.ForecastReview);
        Assert.Equal(2, done.Version);
        Assert.Equal(1, done.CountedActions);
        Assert.Equal(1, done.TurnIndex);
        Assert.Equal(Side.Black, done.SideToMove);
        Assert.Equal(15, done.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Equal(0, done.ConsecutiveTimeouts[Side.Red]);
        Assert.Throws<InvalidOperationException>(() => MatchTurnRules.CompleteForecastReview(done));
        Assert.NotNull(pending.ForecastReview);
    }

    [Fact]
    public void Bot_casts_still_complete_without_waiting_for_a_human_confirmation()
    {
        var state = Position();
        var cast = MatchTurnRules.ApplySkill(state, Side.Red,
            FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0], 16),
            GameJson.Element(new { type = "team_skill", slot = 1 }), _dispatcher);
        Assert.True(cast.Accepted);
        Assert.Null(cast.State.ForecastReview);
        Assert.Equal(1, cast.State.CountedActions);
        Assert.Equal(Side.Black, cast.State.SideToMove);
    }

    private static GameState Position()
    {
        var state = new GameState { PublicRiverStakes = true, Pieces = [
            BotDifficultyTests.Piece(PieceClass.General, Side.Red, 3, 0, 0),
            BotDifficultyTests.Piece(PieceClass.General, Side.Black, 5, 9, 0)] };
        state.SkillStates[Side.Red].Add(new(1, Guid.NewGuid(), 1, 0, FactionSkillHandler.Forecast));
        return MatchTurnRules.Prepare(state);
    }
}

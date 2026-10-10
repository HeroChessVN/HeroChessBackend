using HeroChess.Api.Services;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class MatchActionAvailabilityTests
{
    private static PieceState Piece(PieceClass kind, Side side, int x, int y) => new()
    {
        PieceId = Guid.NewGuid(), HeroId = Guid.NewGuid(), Class = kind, Side = side,
        Position = new BoardPoint(x, y), StartPosition = new BoardPoint(x, y)
    };

    [Fact]
    public void Shield_can_rescue_a_side_with_no_legal_move()
    {
        var state = new GameState { SideToMove = Side.Black, Pieces = new()
        {
            Piece(PieceClass.General, Side.Red, 3, 0), Piece(PieceClass.General, Side.Black, 4, 9),
            Piece(PieceClass.Soldier, Side.Red, 4, 8), Piece(PieceClass.Rook, Side.Red, 3, 8),
            Piece(PieceClass.Rook, Side.Red, 5, 8), Piece(PieceClass.Rook, Side.Red, 4, 6)
        } };
        var engine = new XiangqiRulesEngine();
        Assert.Empty(engine.GenerateLegalActions(state));
        var dispatcher = new CommandSkillDispatcher(new CommandSkillRegistry(new ICommandSkillHandler[] { new KhienHandler() }));
        state.SkillStates[Side.Black].Add(new SkillState(1, Guid.NewGuid(), null, 0, SkillKeys.Khien));
        var rescue = MatchCommandService.FindLegalSkillAction(state, engine, dispatcher);
        Assert.NotNull(rescue);
        Assert.Equal("team_skill", rescue.Value.GetProperty("type").GetString());
        state.SkillStates[Side.Black][0] = state.SkillStates[Side.Black][0] with { CooldownRemaining = 1 };
        Assert.Null(MatchCommandService.FindLegalSkillAction(state, engine, dispatcher));
    }
}

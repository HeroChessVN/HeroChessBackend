using System.Text.Json;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.Rules.Tests;

/// <summary>
/// Visible occupancy blocks placement; concealed enemy snares must not reveal themselves through validation.
/// </summary>
public sealed class SkillStackingTests
{
    [Theory]
    [InlineData(Side.Red, SkillKeys.Thanh)]
    [InlineData(Side.Black, SkillKeys.Thanh)]
    [InlineData(Side.Red, SkillKeys.Rao)]
    [InlineData(Side.Black, SkillKeys.Rao)]
    public void Thanh_and_rao_are_rejected_on_a_cell_holding_a_coc_a_thanh_a_rao_or_a_trap(Side side, string key)
    {
        foreach (var blocker in new[] { SkillKeys.ObstacleKindThDTuongCoc, SkillKeys.ObstacleKindThanh, SkillKeys.ObstacleKindRao })
        {
            var state = State(side);
            state.Obstacles.Add(new ObstacleState(Guid.NewGuid(), Position(side), blocker, 3));
            var result = Place(state, side, key, Position(side));
            Assert.False(result.Accepted, $"{key} must not stack on {blocker}.");
            Assert.Equal("CELL_OCCUPIED", result.Error!.Code);
        }

        var withTrap = State(side);
        withTrap.RiverSnares.Add(new RiverSnare(side, Position(side), 10));
        var trapResult = Place(withTrap, side, key, Position(side));
        Assert.False(trapResult.Accepted, $"{key} must not stack on a trap.");
        Assert.Equal("CELL_OCCUPIED", trapResult.Error!.Code);
    }

    [Theory]
    [InlineData(Side.Red)]
    [InlineData(Side.Black)]
    public void Thanh_and_rao_are_rejected_on_a_cell_holding_a_piece(Side side)
    {
        var state = State(side);
        state.Pieces.Add(new PieceState { PieceId = Guid.NewGuid(), Side = side, Class = PieceClass.Soldier,
            Status = PieceStatus.Alive, Position = Position(side) });
        foreach (var key in new[] { SkillKeys.Thanh, SkillKeys.Rao })
        {
            var result = Place(state, side, key, Position(side));
            Assert.False(result.Accepted);
            Assert.Equal("CELL_OCCUPIED", result.Error!.Code);
        }
    }

    [Theory]
    [InlineData(Side.Red)]
    [InlineData(Side.Black)]
    public void A_coc_stake_is_rejected_on_a_cell_holding_a_coc_a_thanh_a_rao_or_a_trap(Side side)
    {
        foreach (var blocker in new[] { SkillKeys.ObstacleKindThDTuongCoc, SkillKeys.ObstacleKindThanh, SkillKeys.ObstacleKindRao })
        {
            var state = State(side);
            state.Obstacles.Add(new ObstacleState(Guid.NewGuid(), Position(side), blocker, 3));
            var result = BachDangGiang.Execute(state, side, Hero(state).PieceId, Target(Position(side)));
            Assert.False(result.Accepted, $"Cọc must not stack on {blocker}.");
            Assert.Equal("CELL_OCCUPIED", result.Error!.Code);
        }

        var withTrap = State(side);
        withTrap.RiverSnares.Add(new RiverSnare(side, Position(side), 10));
        var trapResult = BachDangGiang.Execute(withTrap, side, Hero(withTrap).PieceId, Target(Position(side)));
        Assert.False(trapResult.Accepted, "Cọc must not stack on a trap.");
        Assert.Equal("CELL_OCCUPIED", trapResult.Error!.Code);
    }

    [Theory]
    [InlineData(Side.Red)]
    [InlineData(Side.Black)]
    public void Traps_are_rejected_on_a_cell_holding_a_coc_a_thanh_a_rao_or_another_trap(Side side)
    {
        foreach (var blocker in new[] { SkillKeys.ObstacleKindThDTuongCoc, SkillKeys.ObstacleKindThanh, SkillKeys.ObstacleKindRao })
        {
            var state = State(side);
            state.Obstacles.Add(new ObstacleState(Guid.NewGuid(), Position(side), blocker, 3));
            var result = PlaceTraps(state, side);
            Assert.False(result.Accepted, $"Bẫy must not stack on {blocker}.");
            Assert.Equal("CELL_OCCUPIED", result.Error!.Code);
        }

        var withTrap = State(side);
        withTrap.RiverSnares.Add(new RiverSnare(side, Position(side), 10));
        Assert.False(PlaceTraps(withTrap, side).Accepted, "Bẫy must not stack on a trap.");
    }

    [Fact]
    public void An_empty_cell_is_still_usable_by_both_sides()
    {
        foreach (var side in new[] { Side.Red, Side.Black })
        {
            var state = State(side);
            Assert.True(Place(state, side, SkillKeys.Thanh, Position(side)).Accepted);
            Assert.True(Place(state, side, SkillKeys.Rao, Position(side)).Accepted);
        }
    }

    [Theory]
    [InlineData(Side.Red)]
    [InlineData(Side.Black)]
    public void Enemy_hidden_traps_do_not_change_placement_or_candidate_targets(Side side)
    {
        var state = State(side);
        var targets = FactionSkillHandler.Targets(state).Select(t => JsonSerializer.Serialize(t)).ToArray();
        state.RiverSnares.Add(new(side == Side.Red ? Side.Black : Side.Red, Position(side), 10));
        state.RiverSnares.Add(new(side == Side.Red ? Side.Black : Side.Red, new(4, 4), 10));
        Assert.True(Place(state, side, SkillKeys.Thanh, Position(side)).Accepted);
        Assert.True(Place(state, side, SkillKeys.Rao, Position(side)).Accepted);
        Assert.True(BachDangGiang.Execute(state, side, Hero(state).PieceId, Target(Position(side))).Accepted);
        var traps = PlaceTraps(state, side);
        Assert.True(traps.Accepted);
        Assert.Equal(2, traps.State.RiverSnares.Count(t => t.Position == Position(side)));
        Assert.Equal(targets, FactionSkillHandler.Targets(state).Select(t => JsonSerializer.Serialize(t)).ToArray());
    }

    private static BoardPoint Position(Side side) => side == Side.Red ? new BoardPoint(2, 4) : new BoardPoint(2, 5);

    private static GameState State(Side side) => new()
    {
        PublicRiverStakes = true,
        SideToMove = side,
        TurnIndex = 1,
        Pieces = [Hero(side), Piece(PieceClass.General, Side.Red, 3, 0), Piece(PieceClass.General, Side.Black, 5, 9)]
    };

    private static PieceState Hero(Side side) => new()
    {
        PieceId = Guid.NewGuid(), Class = PieceClass.Elephant, Side = side, Position = new BoardPoint(0, side == Side.Red ? 0 : 9),
        Status = PieceStatus.Alive, TraitKind = "active", TraitImplementationKey = SkillKeys.ThDTuongCoc,
        TraitParameters = JsonSerializer.SerializeToElement(new { cooldownTurns = 2, durationTurns = 8 })
    };

    /// <summary>Hero of the state, so the piece id matches the one the handler looks for.</summary>
    private static PieceState Hero(GameState state) => state.Pieces.First(piece => piece.TraitImplementationKey == SkillKeys.ThDTuongCoc);

    private static PieceState Piece(PieceClass kind, Side side, int x, int y) => new()
    {
        PieceId = Guid.NewGuid(), Class = kind, Side = side, Position = new BoardPoint(x, y), Status = PieceStatus.Alive
    };

    private static JsonElement Target(BoardPoint position) =>
        JsonSerializer.SerializeToElement(new { position = new { x = position.X, y = position.Y } });

    private static CommandSkillResult Place(GameState state, Side side, string key, BoardPoint position) =>
        key == SkillKeys.Thanh
            ? new ThanhHandler().Execute(new CommandSkillContext(state, side, 1, Guid.NewGuid(), key, 2, Target(position)))
            : new RaoHandler().Execute(new CommandSkillContext(state, side, 1, Guid.NewGuid(), key, 2, Target(position)));

    private static CommandSkillResult PlaceTraps(GameState state, Side side)
    {
        var position = Position(side);
        var target = JsonSerializer.SerializeToElement(new
        {
            positions = new[] { new { x = position.X, y = position.Y }, new { x = 3, y = position.Y }, new { x = 4, y = position.Y } }
        });
        return new FactionSkillHandler(FactionSkillHandler.Trap)
            .Execute(new CommandSkillContext(state, side, 1, Guid.NewGuid(), FactionSkillHandler.Trap, 8, target));
    }
}

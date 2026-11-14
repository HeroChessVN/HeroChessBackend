using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.Rules.Tests;

public sealed class RiverSnareTests
{
    private readonly XiangqiRulesEngine _engine = new();

    [Theory]
    [InlineData(false, false, 6)]
    [InlineData(false, true, 6)]
    [InlineData(true, false, 6)]
    [InlineData(true, true, 6)]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 3)]
    public void Cannon_stops_before_the_intended_capture_with_or_without_a_trait_or_shield(bool trait, bool shield, int screenY)
    {
        var cannon = Piece(PieceClass.Cannon, Side.Black, 2, 8, trait ? SkillKeys.LyThuongKietPhao : null);
        if (shield) cannon.Effects.Add(new(SkillKeys.ShieldEffect, null, 8));
        var victim = Piece(PieceClass.Horse, Side.Red, 2, 2);
        var state = Position(cannon, victim, Piece(PieceClass.Soldier, Side.Black, 2, screenY));
        state.RiverSnares.Add(new(Side.Red, new(2, 5), 10));
        state.RiverSnares.Add(new(Side.Red, new(2, 4), 10));
        var destination = new BoardPoint(2, 2);

        // Hidden snares must not change the preview or let the client probe their locations.
        Assert.Equal(victim.PieceId, Assert.Single(_engine.GenerateLegalActions(state),
            move => move.PieceId == cannon.PieceId && move.To == destination).CapturedPieceId);
        var applied = _engine.ApplyMove(state, new(cannon.PieceId, destination));
        Assert.True(applied.Accepted);
        var stopped = applied.State.Pieces.Single(p => p.PieceId == cannon.PieceId);
        Assert.Equal(new BoardPoint(2, 5), stopped.Position);
        Assert.Equal(PieceStatus.Alive, applied.State.Pieces.Single(p => p.PieceId == victim.PieceId).Status);
        Assert.Equal(destination, applied.State.Pieces.Single(p => p.PieceId == victim.PieceId).Position);
        Assert.Equal(new BoardPoint(2, 4), Assert.Single(applied.State.RiverSnares).Position);
        Assert.Equal(2, state.RiverSnares.Count);
        Assert.Equal(new BoardPoint(2, 8), cannon.Position);

        var ticked = TurnLifecycle.Apply(applied.State, Side.Red).State;
        Assert.Equal(4, ticked.Pieces.Single(p => p.PieceId == cannon.PieceId).Effects.Single(e => e.Code == FactionSkillHandler.Rooted).RemainingTurns);
        Assert.DoesNotContain(_engine.GenerateLegalActions(ticked, Side.Black), move => move.PieceId == cannon.PieceId);
        Assert.Equal(1, ticked.CountedActions);
    }

    [Theory]
    [InlineData(PieceClass.Rook, null, 2, 8, 2, 2)]
    [InlineData(PieceClass.Rook, SkillKeys.LyThuongKietXe, 2, 8, 2, 2)]
    [InlineData(PieceClass.Elephant, "elephant.river_crossing", 0, 6, 2, 4)]
    [InlineData(PieceClass.Soldier, null, 2, 6, 2, 4)]
    public void Other_movement_types_stop_on_the_first_crossed_snare(PieceClass kind, string? key, int fromX, int fromY, int toX, int toY)
    {
        var piece = Piece(kind, Side.Black, fromX, fromY, key);
        if (kind == PieceClass.Soldier) piece.Effects.Add(new(FactionSkillHandler.Rally, null, 6));
        var state = Position(piece);
        var trap = kind == PieceClass.Elephant ? new BoardPoint(1, 5) : new BoardPoint(2, 5);
        state.RiverSnares.Add(new(Side.Red, trap, 10));
        var applied = _engine.ApplyMove(state, new(piece.PieceId, new(toX, toY)));
        Assert.True(applied.Accepted);
        Assert.Equal(trap, applied.State.Pieces.Single(p => p.PieceId == piece.PieceId).Position);
        Assert.Empty(applied.State.RiverSnares);
    }

    [Fact]
    public void Snare_under_a_cannon_screen_stays_dormant_until_the_cell_is_empty()
    {
        var cannon = Piece(PieceClass.Cannon, Side.Black, 2, 8);
        var screen = Piece(PieceClass.Soldier, Side.Black, 2, 5);
        var victim = Piece(PieceClass.Horse, Side.Red, 2, 2);
        var state = Position(cannon, screen, victim);
        state.RiverSnares.Add(new(Side.Red, new(2, 5), 10));
        var applied = _engine.ApplyMove(state, new(cannon.PieceId, new(2, 2)));
        Assert.True(applied.Accepted);
        Assert.Equal(new BoardPoint(2, 2), applied.State.Pieces.Single(p => p.PieceId == cannon.PieceId).Position);
        Assert.Single(applied.State.RiverSnares);
        Assert.Empty(applied.State.Pieces.Single(p => p.PieceId == cannon.PieceId).Effects);

        // The same trap activates normally after the occupying piece leaves the cell.
        state.Pieces.Remove(screen);
        state.Pieces.Add(Piece(PieceClass.Soldier, Side.Black, 2, 6));
        var retried = _engine.ApplyMove(state, new(cannon.PieceId, new(2, 2)));
        Assert.True(retried.Accepted);
        Assert.Equal(new BoardPoint(2, 5), retried.State.Pieces.Single(p => p.PieceId == cannon.PieceId).Position);
        Assert.Empty(retried.State.RiverSnares);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Friendly_and_expired_snares_do_not_intercept(bool expired)
    {
        var rook = Piece(PieceClass.Rook, Side.Black, 2, 8);
        var state = Position(rook);
        state.RiverSnares.Add(new(expired ? Side.Red : Side.Black, new(2, 5), expired ? 0 : 10));
        var applied = _engine.ApplyMove(state, new(rook.PieceId, new(2, 2)));
        Assert.True(applied.Accepted);
        Assert.Equal(new BoardPoint(2, 2), applied.State.Pieces.Single(p => p.PieceId == rook.PieceId).Position);
        Assert.Empty(applied.State.Pieces.Single(p => p.PieceId == rook.PieceId).Effects);
    }

    [Fact]
    public void A_snare_under_the_capture_target_is_dormant_instead_of_stacking_pieces()
    {
        var cannon = Piece(PieceClass.Cannon, Side.Black, 2, 8);
        var victim = Piece(PieceClass.Horse, Side.Red, 2, 4);
        var state = Position(cannon, victim, Piece(PieceClass.Soldier, Side.Black, 2, 6));
        state.RiverSnares.Add(new(Side.Red, new(2, 4), 10));
        var applied = _engine.ApplyMove(state, new(cannon.PieceId, new(2, 4)));
        Assert.True(applied.Accepted);
        Assert.Equal(PieceStatus.Captured, applied.State.Pieces.Single(p => p.PieceId == victim.PieceId).Status);
        Assert.Equal(new BoardPoint(2, 4), applied.State.Pieces.Single(p => p.PieceId == cannon.PieceId).Position);
        Assert.Single(applied.State.RiverSnares);
        Assert.Empty(applied.State.Pieces.Single(p => p.PieceId == cannon.PieceId).Effects);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_first_hazard_on_the_route_resolves_before_a_later_hazard(bool snareFirst)
    {
        var rook = Piece(PieceClass.Rook, Side.Black, 2, 8);
        var state = Position(rook);
        state.RiverSnares.Add(new(Side.Red, new(2, snareFirst ? 5 : 4), 10));
        var id = Guid.NewGuid();
        state.Obstacles.Add(new(id, new(2, snareFirst ? 4 : 5), SkillKeys.ObstacleKindThDTuongCoc, 8));
        state.ObstacleMetadata[id] = new() { Placer = Side.Red };
        state.ThdTuongCocStakes[Guid.NewGuid()] = id;
        var applied = _engine.ApplyMove(state, new(rook.PieceId, new(2, 2)));
        Assert.True(applied.Accepted);
        var after = applied.State.Pieces.Single(p => p.PieceId == rook.PieceId);
        Assert.Equal(snareFirst ? PieceStatus.Alive : PieceStatus.Captured, after.Status);
        Assert.Equal(snareFirst ? new BoardPoint(2, 5) : (BoardPoint?)null, after.Position);
        Assert.Equal(snareFirst ? 0 : 1, applied.State.RiverSnares.Count);
    }

    [Fact]
    public void Quang_trung_active_movement_stops_at_the_snare_and_still_starts_cooldown()
    {
        var king = new PieceState { PieceId = Guid.NewGuid(), Class = PieceClass.General, Side = Side.Black,
            Position = new(1, 5), TraitImplementationKey = SkillKeys.QuangTrungSpecialMove };
        king.TraitState[SkillKeys.QuangTrungCooldownKey] = 1;
        var state = new GameState { PublicRiverStakes = true, SideToMove = Side.Black,
            Pieces = [king, Piece(PieceClass.General, Side.Red, 3, 0)] };
        state.RiverSnares.Add(new(Side.Red, new(2, 5), 10));
        var applied = _engine.ApplyHeroSpecialMove(state, new(king.PieceId, new(4, 5)));
        Assert.True(applied.Accepted);
        var after = applied.State.Pieces.Single(p => p.PieceId == king.PieceId);
        Assert.Equal(new BoardPoint(2, 5), after.Position);
        Assert.Equal(SkillKeys.QuangTrungCooldownTurns, after.TraitState[SkillKeys.QuangTrungCooldownRemainingKey]);
        Assert.Contains(after.Effects, e => e.Code == FactionSkillHandler.Rooted);
    }

    [Fact]
    public void An_intercepted_escape_that_leaves_the_general_in_check_is_an_immediate_loss()
    {
        var horse = Piece(PieceClass.Horse, Side.Black, 3, 6);
        var attacker = Piece(PieceClass.Rook, Side.Red, 4, 0);
        var blackKing = Piece(PieceClass.General, Side.Black, 4, 9);
        var redKing = Piece(PieceClass.General, Side.Red, 3, 0);
        var state = new GameState { PublicRiverStakes = true, SideToMove = Side.Black,
            Pieces = [horse, attacker, blackKing, redKing] };
        state.RiverSnares.Add(new(Side.Red, new(3, 5), 10));
        var move = new MoveAction(horse.PieceId, new(4, 4));
        Assert.Contains(_engine.GenerateLegalActions(state), m => m.PieceId == horse.PieceId && m.To == move.To);
        var applied = _engine.ApplyMove(state, move);
        Assert.True(applied.Accepted);
        Assert.Equal(new BoardPoint(3, 5), applied.State.Pieces.Single(p => p.PieceId == horse.PieceId).Position);
        Assert.Equal("red_win", applied.State.Result);
        Assert.Equal("river_snare", applied.State.EndReason);
        Assert.Equal(1, applied.State.CountedActions);
    }

    private static GameState Position(params PieceState[] pieces) => new()
    {
        PublicRiverStakes = true, SideToMove = Side.Black,
        Pieces = [.. pieces, Piece(PieceClass.General, Side.Red, 3, 0), Piece(PieceClass.General, Side.Black, 5, 9)]
    };

    private static PieceState Piece(PieceClass kind, Side side, int x, int y, string? key = null) => new()
    {
        PieceId = Guid.NewGuid(), Class = kind, Side = side, Position = new(x, y), MovementImplementationKey = key
    };
}

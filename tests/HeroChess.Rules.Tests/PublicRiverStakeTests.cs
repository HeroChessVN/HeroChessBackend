using System.Text.Json;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.Rules.Tests;

public sealed class PublicRiverStakeTests
{
    private readonly XiangqiRulesEngine _engine = new();

    [Fact]
    public void Hidden_snare_at_horse_leg_consumes_one_trap_and_locks_for_four_turns()
    {
        var horse = Piece(PieceClass.Horse, Side.Black, 2, 6);
        var state = Position(horse);
        state.RiverSnares.Add(new(Side.Red, new(2, 5), 6));
        state.RiverSnares.Add(new(Side.Red, new(3, 4), 6));
        var applied = _engine.ApplyMove(state, new(horse.PieceId, new(3, 4)));
        Assert.True(applied.Accepted);
        Assert.Equal(new BoardPoint(3, 4), applied.State.Pieces.Single(p => p.PieceId == horse.PieceId).Position);
        Assert.Equal(new BoardPoint(3, 4), Assert.Single(applied.State.RiverSnares).Position);
        var next = TurnLifecycle.Apply(applied.State, Side.Red).State;
        Assert.Equal(4, Assert.Single(next.Pieces.Single(p => p.PieceId == horse.PieceId).Effects).RemainingTurns);
        Assert.DoesNotContain(_engine.GenerateLegalActions(next, Side.Black), m => m.PieceId == horse.PieceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rally_soldier_can_move_two_only_with_an_empty_middle(bool blocked)
    {
        var pawn = Piece(PieceClass.Soldier, Side.Black, 2, 4);
        pawn.Effects.Add(new(FactionSkillHandler.Rally, null, 6));
        var state = blocked ? Position(pawn, Piece(PieceClass.Soldier, Side.Black, 3, 4)) : Position(pawn);
        Assert.Equal(!blocked, _engine.GenerateLegalActions(state).Any(m => m.PieceId == pawn.PieceId && m.To == new BoardPoint(4, 4)));
        Assert.DoesNotContain(_engine.GenerateLegalActions(state), m => m.PieceId == pawn.PieceId && m.To == new BoardPoint(2, 6));
    }

    [Theory]
    [InlineData(5)] // intercepted before reaching the victim
    [InlineData(4)] // stake and victim share the destination
    public void Stake_prevents_capture_and_leaves_the_victim_in_place(int stakeY)
    {
        var attacker = Piece(PieceClass.Rook, Side.Black, 2, 7);
        var victim = Piece(PieceClass.Soldier, Side.Red, 2, 4);
        var state = Position(attacker, victim);
        AddStake(state, Side.Red, new(2, stakeY));
        var destination = new BoardPoint(2, 4);
        Assert.Null(Assert.Single(_engine.GenerateLegalActions(state), m => m.PieceId == attacker.PieceId && m.To == destination).CapturedPieceId);

        var applied = _engine.ApplyMove(state, new(attacker.PieceId, destination));
        Assert.True(applied.Accepted);
        Assert.Equal(new BoardPoint(2, stakeY), applied.StakeHit!.Position);
        Assert.Equal(PieceStatus.Captured, applied.State.Pieces.Single(p => p.PieceId == attacker.PieceId).Status);
        Assert.Equal(destination, applied.State.Pieces.Single(p => p.PieceId == victim.PieceId).Position);
        Assert.Single(applied.State.Obstacles);
        Assert.Equal(PieceStatus.Alive, attacker.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Horse_checks_its_leg_first_and_shield_protects_the_whole_move(bool shielded)
    {
        var horse = Piece(PieceClass.Horse, Side.Black, 2, 6);
        if (shielded) horse.Effects.Add(new(SkillKeys.ShieldEffect, null, 2));
        var state = Position(horse);
        AddStake(state, Side.Red, new(2, 5));
        AddStake(state, Side.Red, new(3, 4));
        var applied = _engine.ApplyMove(state, new(horse.PieceId, new(3, 4)));
        Assert.True(applied.Accepted);
        Assert.Equal(shielded ? (BoardPoint?)null : new BoardPoint(2, 5), applied.StakeHit?.Position);
        Assert.Equal(shielded ? PieceStatus.Alive : PieceStatus.Captured, applied.State.Pieces.Single(p => p.PieceId == horse.PieceId).Status);
    }

    [Fact]
    public void Losing_the_piece_between_generals_to_a_stake_is_an_immediate_loss()
    {
        var rook = Piece(PieceClass.Rook, Side.Black, 4, 7);
        var state = new GameState { PublicRiverStakes = true, SideToMove = Side.Black,
            Pieces = [rook, Piece(PieceClass.General, Side.Black, 4, 9), Piece(PieceClass.General, Side.Red, 4, 0)] };
        AddStake(state, Side.Red, new(4, 5));
        var applied = _engine.ApplyMove(state, new(rook.PieceId, new(4, 4)));
        Assert.True(applied.Accepted);
        Assert.Equal("red_win", applied.State.Result);
        Assert.Equal("river_stake", applied.State.EndReason);
        Assert.Equal(1, applied.State.CountedActions);
    }

    [Fact]
    public void A_rook_intercepted_by_a_public_stake_does_not_give_check()
    {
        var state = Position(Piece(PieceClass.Rook, Side.Black, 3, 7));
        Assert.True(_engine.IsInCheck(state, Side.Red));
        AddStake(state, Side.Red, new(3, 5));
        Assert.False(_engine.IsInCheck(state, Side.Red));
    }

    [Fact]
    public void Snapshot_parameters_and_existing_stake_control_placement()
    {
        var red = StakeHero(Side.Red, 2, 2);
        var black = StakeHero(Side.Black, 6, 7);
        var state = Position(red, black);
        var target = JsonSerializer.SerializeToElement(new { position = new { x = 4, y = 5 } });
        var placed = BachDangGiang.Execute(state, Side.Red, red.PieceId, target);
        Assert.True(placed.Accepted);
        Assert.Equal(8, Assert.Single(placed.State.Obstacles).RemainingLifetime);
        Assert.Equal(2, placed.State.Pieces.Single(p => p.PieceId == red.PieceId).TraitState[SkillKeys.HeroCooldownRemainingKey]);
        var ticked = TurnLifecycle.Apply(TurnLifecycle.Apply(placed.State, Side.Black).State, Side.Red).State;
        Assert.Equal("STAKE_ALREADY_ACTIVE", BachDangGiang.Execute(ticked, Side.Red, red.PieceId, target).Error!.Code);
        Assert.Equal("CELL_OCCUPIED", BachDangGiang.Execute(ticked, Side.Black, black.PieceId, target).Error!.Code);
    }

    private static GameState Position(params PieceState[] pieces) => new() { PublicRiverStakes = true, SideToMove = Side.Black,
        Pieces = [.. pieces, Piece(PieceClass.General, Side.Red, 3, 0), Piece(PieceClass.General, Side.Black, 5, 9)] };

    private static PieceState Piece(PieceClass kind, Side side, int x, int y) => new()
        { PieceId = Guid.NewGuid(), Class = kind, Side = side, Position = new(x, y) };

    private static PieceState StakeHero(Side side, int x, int y) => new() { PieceId = Guid.NewGuid(), Class = PieceClass.Elephant,
        Side = side, Position = new(x, y), TraitKind = "active", TraitImplementationKey = SkillKeys.ThDTuongCoc,
        TraitParameters = JsonSerializer.SerializeToElement(new { cooldownTurns = 2, durationTurns = 8 }) };

    private static void AddStake(GameState state, Side owner, BoardPoint position)
    {
        var id = Guid.NewGuid();
        state.Obstacles.Add(new(id, position, SkillKeys.ObstacleKindThDTuongCoc, 3));
        state.ObstacleMetadata[id] = new() { Placer = owner };
        state.ThdTuongCocStakes[Guid.NewGuid()] = id;
    }
}

using System.Text.Json;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.Rules.Tests;

public sealed class NewSkillGameplayTests
{
    [Fact]
    public void Frozen_display_data_survives_state_clone()
    {
        var piece = Piece(PieceClass.Elephant, Side.Red, 2, 2);
        var state = new GameState { Pieces = new() { new PieceState
        {
            PieceId = piece.PieceId, HeroId = piece.HeroId, Side = piece.Side, Class = piece.Class,
            Position = piece.Position, StartPosition = piece.StartPosition,
            HeroName = "Trần Hưng Đạo", TraitName = "Bạch Đằng Giang",
            TraitParameters = JsonSerializer.SerializeToElement(new { cooldownTurns = 5 })
        } } };
        state.SkillStates[Side.Red].Add(new SkillState(1, Guid.NewGuid(), null, 0, SkillKeys.Khien,
            "Khiên", "Bảo vệ quân đồng minh", JsonSerializer.SerializeToElement(new { durationTurns = 4 })));
        var clone = state.Clone();
        Assert.Equal("Trần Hưng Đạo", clone.Pieces[0].HeroName);
        Assert.Equal(5, clone.Pieces[0].TraitParameters?.GetProperty("cooldownTurns").GetInt32());
        Assert.Equal("Khiên", clone.SkillStates[Side.Red][0].Name);
        Assert.Equal(4, clone.SkillStates[Side.Red][0].Parameters?.GetProperty("durationTurns").GetInt32());
    }

    private static PieceState Piece(PieceClass kind, Side side, int x, int y, string? trait = null) => new()
    {
        PieceId = Guid.NewGuid(), HeroId = Guid.NewGuid(), Class = kind, Side = side,
        Position = new BoardPoint(x, y), StartPosition = new BoardPoint(x, y),
        TraitKind = trait is null ? null : "active", TraitImplementationKey = trait
    };

    [Fact]
    public void BachDangGiang_triggers_only_on_enemy_landing_without_hiding_legal_move()
    {
        var hero = Piece(PieceClass.Elephant, Side.Red, 2, 2, SkillKeys.ThDTuongCoc);
        var enemy = Piece(PieceClass.Soldier, Side.Black, 4, 6);
        var state = new GameState { Pieces = new() { hero, enemy, Piece(PieceClass.General, Side.Red, 3, 0), Piece(PieceClass.General, Side.Black, 5, 9) } };
        var target = JsonSerializer.SerializeToElement(new { position = new { x = 4, y = 5 } });
        var placed = BachDangGiang.Execute(state, Side.Red, hero.PieceId, target);
        Assert.True(placed.Accepted);
        Assert.Empty(state.Obstacles);
        Assert.Equal(5, placed.State.Pieces.Single(p => p.PieceId == hero.PieceId).TraitState[SkillKeys.HeroCooldownRemainingKey]);

        placed.State.SideToMove = Side.Black;
        var rules = new XiangqiRulesEngine();
        var withStake = rules.GenerateLegalActions(placed.State).Where(move => move.PieceId == enemy.PieceId).Select(move => move.To).ToArray();
        var withoutStake = rules.GenerateLegalActions(WithSide(state, Side.Black)).Where(move => move.PieceId == enemy.PieceId).Select(move => move.To).ToArray();
        Assert.Equal(withoutStake.OrderBy(x => x.X).ThenBy(x => x.Y), withStake.OrderBy(x => x.X).ThenBy(x => x.Y));
        Assert.Contains(new BoardPoint(4, 5), withStake);
        var moved = rules.ApplyMove(placed.State, new MoveAction(enemy.PieceId, new BoardPoint(4, 5)));
        Assert.True(moved.Accepted);
        Assert.Equal(PieceStatus.Captured, moved.State.Pieces.Single(p => p.PieceId == enemy.PieceId).Status);
    }

    private static GameState WithSide(GameState state, Side side)
    {
        var copy = state.Clone();
        copy.SideToMove = side;
        return copy;
    }

    [Fact]
    public void Shield_prevents_capture_and_check_until_four_shared_turns_pass()
    {
        var general = Piece(PieceClass.General, Side.Red, 3, 0);
        var rook = Piece(PieceClass.Rook, Side.Black, 3, 5);
        var state = new GameState { Pieces = new() { general, rook, Piece(PieceClass.General, Side.Black, 5, 9) } };
        var rules = new XiangqiRulesEngine();
        Assert.True(rules.IsInCheck(state, Side.Red));
        var target = JsonSerializer.SerializeToElement(new { pieceId = general.PieceId });
        var shielded = new KhienHandler().Execute(new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(), SkillKeys.Khien, 8, target));
        Assert.True(shielded.Accepted);
        Assert.False(rules.IsInCheck(shielded.State, Side.Red));
        var current = shielded.State;
        for (var i = 0; i < 3; i++)
        {
            current = TurnLifecycle.Apply(current, i % 2 == 0 ? Side.Black : Side.Red).State;
            Assert.False(rules.IsInCheck(current, Side.Red));
        }
        current = TurnLifecycle.Apply(current, Side.Red).State;
        Assert.True(rules.IsInCheck(current, Side.Red));
    }

    [Fact]
    public void Shielded_piece_cannot_be_captured_and_hidden_stake_does_not_block_placement()
    {
        var soldier = Piece(PieceClass.Soldier, Side.Red, 4, 4);
        var rook = Piece(PieceClass.Rook, Side.Black, 4, 5);
        soldier.Effects.Add(new EffectState(SkillKeys.ShieldEffect, null, 2));
        var state = new GameState { SideToMove = Side.Black, Pieces = new()
        {
            soldier, rook, Piece(PieceClass.General, Side.Red, 3, 0), Piece(PieceClass.General, Side.Black, 5, 9)
        } };
        Assert.DoesNotContain(new XiangqiRulesEngine().GenerateLegalActions(state), move =>
            move.PieceId == rook.PieceId && move.To == new BoardPoint(4, 4));

        var stake = Guid.NewGuid();
        state.Obstacles.Add(new ObstacleState(stake, new BoardPoint(6, 5), SkillKeys.ObstacleKindThDTuongCoc, 2));
        state.ObstacleMetadata[stake] = new ObstacleMetadata { Placer = Side.Red };
        var target = JsonSerializer.SerializeToElement(new { position = new { x = 6, y = 5 } });
        var ctx = new CommandSkillContext(state, Side.Black, 1, Guid.NewGuid(), SkillKeys.Rao, 6, target);
        Assert.True(new RaoHandler().Execute(ctx).Accepted);
    }

    [Fact]
    public void Shield_blocks_stake_and_stake_remains_until_its_timer_expires()
    {
        var soldier = Piece(PieceClass.Soldier, Side.Black, 4, 6);
        soldier.Effects.Add(new EffectState(SkillKeys.ShieldEffect, null, 2));
        var stake = Guid.NewGuid();
        var state = new GameState { SideToMove = Side.Black, Pieces = new()
        {
            soldier, Piece(PieceClass.General, Side.Red, 3, 0), Piece(PieceClass.General, Side.Black, 5, 9)
        } };
        state.Obstacles.Add(new ObstacleState(stake, new BoardPoint(4, 5), SkillKeys.ObstacleKindThDTuongCoc, 3));
        state.ObstacleMetadata[stake] = new ObstacleMetadata { Placer = Side.Red };
        var moved = new XiangqiRulesEngine().ApplyMove(state, new MoveAction(soldier.PieceId, new BoardPoint(4, 5)));
        Assert.True(moved.Accepted);
        Assert.Equal(PieceStatus.Alive, moved.State.Pieces.Single(p => p.PieceId == soldier.PieceId).Status);
        Assert.Contains(moved.State.Obstacles, o => o.ObstacleId == stake);
    }

    [Fact]
    public void QuangTrung_cooldown_does_not_change_command_skill()
    {
        var general = Piece(PieceClass.General, Side.Red, 4, 0);
        general.TraitState[SkillKeys.QuangTrungCooldownRemainingKey] = 1;
        var state = new GameState { Pieces = new() { general }, SkillStates = new()
        {
            [Side.Red] = new() { new SkillState(1, Guid.NewGuid(), null, 4, SkillKeys.Thanh) },
            [Side.Black] = new()
        } };
        var next = TurnLifecycle.Apply(state, Side.Black).State;
        Assert.Equal(1, next.Pieces[0].TraitState[SkillKeys.QuangTrungCooldownKey]);
        Assert.Equal(0, next.Pieces[0].TraitState[SkillKeys.QuangTrungCooldownRemainingKey]);
        Assert.Equal(3, next.SkillStates[Side.Red][0].CooldownRemaining);
    }
}

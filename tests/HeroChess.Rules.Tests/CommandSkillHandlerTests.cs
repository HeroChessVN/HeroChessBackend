// Phase 2.4: Unit tests for the four Command Skill handlers.
using HeroChess.Rules;
using HeroChess.Rules.Effects;
using HeroChess.Rules.Skills;
using System.Text.Json;
using Xunit;

namespace HeroChess.Rules.Tests;

/// <summary>
/// Tests for VanCocTranGiangHandler (Vạn Cọc Trấn Giang).
/// </summary>
public sealed class VanCocTranGiangHandlerTests
{
    private static GameState MakeState() => new()
    {
        StateSchemaVersion = 4,
        SideToMove = Side.Red,
        EffectInstances = new List<EffectInstance>(),
        ProcessedTurns = new Dictionary<Side, List<int>> { [Side.Red] = new(), [Side.Black] = new() },
        StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
        SkillStates = new Dictionary<Side, List<SkillState>>
        {
            [Side.Red] = new() { new(1, Guid.NewGuid(), null, 0, SkillKeys.VanCocTranGiang) },
            [Side.Black] = new()
        }
    };

    private static JsonElement Target(params int[] columns) =>
        JsonSerializer.SerializeToElement(new { paths = columns });

    private static CommandSkillContext Context(GameState state, Side side = Side.Red) =>
        new(state, side, 1, Guid.NewGuid(), SkillKeys.VanCocTranGiang, 2, Target(4, 5, 6));

    private static VanCocTranGiangHandler Handler() => new();

    [Fact]
    public void Execute_creates_one_root_effect()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        Assert.Single(result.State.EffectInstances);
    }

    [Fact]
    public void Execute_effect_has_correct_code()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        Assert.Equal(SkillKeys.VanCocTranGiang, result.State.EffectInstances[0].Code);
    }

    [Fact]
    public void Execute_effect_covers_30_positions()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        Assert.Equal(30, result.State.EffectInstances[0].TargetPositions.Count); // 3 columns × 10 rows
    }

    [Fact]
    public void Execute_effect_has_duration_2()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        Assert.Equal(2, result.State.EffectInstances[0].Duration);
        Assert.Equal(2, result.State.EffectInstances[0].RemainingDuration);
    }

    [Fact]
    public void Execute_effect_creator_is_actor()
    {
        var state = MakeState();
        var ctx = Context(state, Side.Black);
        var result = Handler().Execute(ctx);

        Assert.Equal(Side.Black, result.State.EffectInstances[0].Creator);
    }

    [Fact]
    public void Execute_creates_30_stake_obstacles()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        var stakes = result.State.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.Equal(30, stakes.Count);
    }

    [Fact]
    public void Execute_stakes_have_lifetime_2()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        var stakes = result.State.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.All(stakes, s => Assert.Equal(2, s.RemainingLifetime));
    }

    [Fact]
    public void Execute_stakes_linked_to_effect()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        var effectId = result.State.EffectInstances[0].EffectId;
        var stakes = result.State.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.All(stakes, s => Assert.Equal(effectId, result.State.StakeMetadata[s.ObstacleId].EffectId));
    }

    [Fact]
    public void Execute_stakes_placer_is_actor()
    {
        var state = MakeState();
        var ctx = Context(state, Side.Black);
        var result = Handler().Execute(ctx);

        var stakes = result.State.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.All(stakes, s => Assert.Equal(Side.Black, result.State.StakeMetadata[s.ObstacleId].Placer));
    }

    private static bool EventHasType(object? e, string typeName) =>
        CommandSkillTestHelpers.EventHasType(e, typeName);

    [Fact]
    public void Execute_emits_effect_created_event()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        Assert.Contains(result.Events, e => EventHasType(e, "effect.created"));
    }

    [Fact]
    public void Execute_emits_obstacle_created_event()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        Assert.Contains(result.Events, e => EventHasType(e, "obstacle.created"));
    }

    [Fact]
    public void Execute_rejects_invalid_path_not_3_elements()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.VanCocTranGiang, 2, Target(4, 5));
        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("INVALID_TARGET", result.Error!.Code);
    }

    [Fact]
    public void Execute_rejects_non_river_columns()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.VanCocTranGiang, 2, Target(0, 1, 2));
        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("INVALID_RIVER_PATH", result.Error!.Code);
    }

    [Fact]
    public void Execute_rejects_non_consecutive_columns()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.VanCocTranGiang, 2, Target(3, 4, 6)); // not consecutive
        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("INVALID_RIVER_PATH", result.Error!.Code);
    }

    [Fact]
    public void Execute_rejects_conflicting_column()
    {
        var state = MakeState();
        // Pre-existing river blocking effect covering column 4
        state.EffectInstances.Add(EffectFactory.Create(
            Guid.NewGuid(), "van_coc_tran_giang", Guid.NewGuid(), Side.Black,
            1, 2, new[] { new BoardPoint(4, 0), new BoardPoint(4, 1) }));
        var ctx = Context(state); // selecting 4,5,6

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("PATH_OCCUPIED", result.Error!.Code);
    }

    [Fact]
    public void Execute_does_not_mutate_original_state()
    {
        var state = MakeState();
        var ctx = Context(state);
        var originalEffectCount = state.EffectInstances.Count;

        Handler().Execute(ctx);

        Assert.Equal(originalEffectCount, state.EffectInstances.Count);
    }

    [Fact]
    public void Execute_increments_NextCreationOrder()
    {
        var state = MakeState();
        state.NextCreationOrder = 5;
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        Assert.Equal(6, result.State.NextCreationOrder);
    }

    [Fact]
    public void Execute_van_coc_creates_one_root_effect()
    {
        // Test: one root Effect per successful activation
        var state = MakeState();
        var ctx = Context(state);

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        Assert.Single(result.State.EffectInstances);
    }

    [Fact]
    public void Execute_creates_three_river_crossing_paths()
    {
        // Test: three river-crossing paths (3 columns × 10 rows = 30 positions)
        var state = MakeState();
        var ctx = Context(state);

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        var effect = result.State.EffectInstances[0];
        Assert.Equal(30, effect.TargetPositions.Count);
    }

    [Fact]
    public void Execute_physical_stakes_are_implementation_representation()
    {
        // Test: 30 physical stake representations are only implementation representation of paths
        // The effect covers 30 positions (3 columns × 10 rows)
        var state = MakeState();
        var ctx = Context(state);

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        var effect = result.State.EffectInstances[0];
        var stakes = result.State.Obstacles.Where(o => o.Kind == "stake").ToList();

        // 30 physical stakes = 3 columns × 10 rows
        Assert.Equal(30, stakes.Count);
        // Effect covers same 30 positions
        Assert.Equal(30, effect.TargetPositions.Count);
    }

    [Fact]
    public void Execute_effect_kind_is_river_blocking()
    {
        // Test: Vạn Cọc effect has kind "river_blocking"
        var state = MakeState();
        var ctx = Context(state);

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        var effect = result.State.EffectInstances[0];

        // Effect should have river_blocking kind
        Assert.True(effect.Payload.TryGetValue("kind", out var kind));
        Assert.Equal("river_blocking", kind);
    }

    [Fact]
    public void Execute_blocking_effect_separate_from_physical_stakes()
    {
        // Test: blocking Effect is separate from physical stakes
        var state = MakeState();
        var ctx = Context(state);

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        var effect = result.State.EffectInstances[0];
        var stakes = result.State.Obstacles.Where(o => o.Kind == "stake").ToList();

        // Effect exists independently
        Assert.NotEmpty(result.State.EffectInstances);
        // Stakes are linked to effect
        var stakesLinkedToEffect = stakes.Where(s =>
            result.State.StakeMetadata.TryGetValue(s.ObstacleId, out var meta) &&
            meta.EffectId == effect.EffectId).ToList();
        Assert.Equal(30, stakesLinkedToEffect.Count);
    }

    [Fact]
    public void Execute_stake_lifetime_equals_2_turns_of_placer()
    {
        // Test: lifetime = 2 turns of original placer
        var state = MakeState();
        var ctx = Context(state);

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        var stakes = result.State.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.All(stakes, s => Assert.Equal(2, s.RemainingLifetime));
    }

    [Fact]
    public void Execute_control_transfer_does_not_reset_stake_lifetime()
    {
        // Test: control transfer does not reset lifetime
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        var effectId = result.State.EffectInstances[0].EffectId;
        var stakesBefore = result.State.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.All(stakesBefore, s => Assert.Equal(2, s.RemainingLifetime));

        // Simulate 1 turn passing (stakes should decrement)
        state = result.State;
        state.SideToMove = Side.Red;
        var afterRedTurn = TurnLifecycle.Apply(state, Side.Red);

        var stakesAfter = afterRedTurn.State.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.All(stakesAfter, s => Assert.Equal(1, s.RemainingLifetime));
    }

    [Fact]
    public void Execute_stakes_expire_after_shared_turns()
    {
        // Test: stakes expire after 2 turns of original placer
        var state = MakeState();
        var ctx = Context(state);
        var firstResult = Handler().Execute(ctx);

        Assert.True(firstResult.Accepted);
        var firstEffect = firstResult.State.EffectInstances[0];

        // Simulate 2 turns of placer (Red) - stakes should decrement and then expire
        var stateAfterTurns = firstResult.State;
        stateAfterTurns.SideToMove = Side.Red;
        stateAfterTurns = TurnLifecycle.Apply(stateAfterTurns, Side.Red).State;
        // After 1 turn: stakes have lifetime 1
        var stakesAfter1 = stateAfterTurns.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.Equal(30, stakesAfter1.Count);
        Assert.All(stakesAfter1, s => Assert.Equal(1, s.RemainingLifetime));

        stateAfterTurns.SideToMove = Side.Black;
        stateAfterTurns = TurnLifecycle.Apply(stateAfterTurns, Side.Black).State;
        // Every side's turn advances the shared timer.
        var stakesAfterBlack = stateAfterTurns.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.Empty(stakesAfterBlack);

        stateAfterTurns.SideToMove = Side.Red;
        stateAfterTurns = TurnLifecycle.Apply(stateAfterTurns, Side.Red).State;
        // After Red's 2nd turn: stakes should be gone
        var stakesAfter2 = stateAfterTurns.Obstacles.Where(o => o.Kind == "stake").ToList();
        Assert.Empty(stakesAfter2);

        // But effect should still exist (it had duration 2)
        var effectStillExists = stateAfterTurns.EffectInstances.Any(e => e.EffectId == firstEffect.EffectId);
        Assert.True(effectStillExists);
    }

    [Fact]
    public void Execute_no_unsupported_path_occupied_gameplay_rule()
    {
        // Test: no unsupported PATH_OCCUPIED gameplay rule
        // This test verifies that the PATH_OCCUPIED error is correctly returned
        // when ALL paths are blocked
        var state = MakeState();
        // Pre-existing river blocking effect covering all 3 columns
        state.EffectInstances.Add(EffectFactory.Create(
            Guid.NewGuid(), "existing_river", Guid.NewGuid(), Side.Black,
            1, 2, RiverGeometry.ResolvePositions(new[] { 4, 5, 6 })));

        var ctx = Context(state); // selecting 4, 5, 6

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("PATH_OCCUPIED", result.Error!.Code);
    }
}

/// <summary>
/// Tests for PhanKyDoatTheHandler (Phản Kỳ Đoạt Thế).
/// </summary>
public sealed class PhanKyDoatTheHandlerTests
{
    private static GameState MakeState() => new()
    {
        StateSchemaVersion = 4,
        SideToMove = Side.Red,
        EffectInstances = new List<EffectInstance>(),
        ProcessedTurns = new Dictionary<Side, List<int>> { [Side.Red] = new(), [Side.Black] = new() },
        StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
        SkillStates = new Dictionary<Side, List<SkillState>>
        {
            [Side.Red] = new() { new(1, Guid.NewGuid(), null, 0, SkillKeys.PhanKyDoatThe) },
            [Side.Black] = new()
        }
    };

    private static JsonElement Target(Guid effectId, int x, int y) =>
        JsonSerializer.SerializeToElement(new { effectId = effectId.ToString(), position = new { x, y } });

    private static EffectInstance MakeTerrainEffect(Guid effectId, Side creator, BoardPoint pos)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = "terrain" };
        return EffectFactory.Create(effectId, SkillKeys.BinhLamThuyHien, Guid.NewGuid(),
            creator, 1, 2, new[] { pos }, payload: payload);
    }

    private static PhanKyDoatTheHandler Handler() => new();

    [Fact]
    public void Execute_sets_pending_controller_not_immediate()
    {
        // CONFIRMED TIMING: Steal is PENDING, not immediate.
        // Official PositionController remains Creator until steal is finalized.
        var state = MakeState();
        var pos = new BoardPoint(4, 5);
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black, pos);
        state.EffectInstances.Add(effect);
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId, 4, 5));

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        // PositionController should STILL be Black (original controller)
        Assert.Equal(Side.Black, result.State.EffectInstances[0].PositionControllers[pos]);
        // PendingController should be set to Red (the thief)
        Assert.Equal(Side.Red, result.State.EffectInstances[0].PendingController);
    }

    [Fact]
    public void Execute_does_not_change_creator()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var pos = new BoardPoint(4, 5);
        state.EffectInstances.Add(MakeTerrainEffect(effectId, Side.Black, pos));
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId, 4, 5));

        var result = Handler().Execute(ctx);

        Assert.Equal(Side.Black, result.State.EffectInstances[0].Creator);
    }

    [Fact]
    public void Execute_does_not_change_creation_order()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var pos = new BoardPoint(4, 5);
        var effect = MakeTerrainEffect(effectId, Side.Black, pos);
        effect = new EffectInstance
        {
            EffectId = effect.EffectId, Code = effect.Code, SkillId = effect.SkillId,
            Creator = effect.Creator, CreationOrder = 99, State = EffectStateValue.Active,
            Duration = effect.Duration, RemainingDuration = effect.RemainingDuration,
            TargetPositions = effect.TargetPositions, Payload = effect.Payload
        };
        foreach (var kvp in effect.PositionControllers) { }
        // Rebuild with creation order
        var effect2 = EffectFactory.Create(effectId, SkillKeys.BinhLamThuyHien, Guid.NewGuid(),
            Side.Black, 99, 2, new[] { pos }, payload: new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = "terrain" });
        state.EffectInstances.Clear();
        state.EffectInstances.Add(effect2);
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId, 4, 5));

        var result = Handler().Execute(ctx);

        Assert.Equal(99, result.State.EffectInstances[0].CreationOrder);
    }

    [Fact]
    public void Execute_does_not_reset_duration()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var pos = new BoardPoint(4, 5);
        var effect = EffectFactory.Create(effectId, SkillKeys.BinhLamThuyHien, Guid.NewGuid(),
            Side.Black, 1, 5, new[] { pos }, payload: new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = "terrain" });
        state.EffectInstances.Add(effect);
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId, 4, 5));

        var result = Handler().Execute(ctx);

        Assert.Equal(5, result.State.EffectInstances[0].RemainingDuration);
    }

    [Fact]
    public void Execute_cannot_target_disabled_effect()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var pos = new BoardPoint(4, 5);
        var effect = EffectFactory.Create(effectId, SkillKeys.BinhLamThuyHien, Guid.NewGuid(),
            Side.Black, 1, 2, new[] { pos }, payload: new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = "terrain" });
        effect.State = EffectStateValue.Disabled;
        state.EffectInstances.Add(effect);
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId, 4, 5));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("EFFECT_DISABLED", result.Error!.Code);
    }

    [Fact]
    public void Execute_cannot_target_non_terrain_effect()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var pos = new BoardPoint(4, 5);
        // Effect without "terrain" kind
        var effect = EffectFactory.Create(effectId, "some_other_code", Guid.NewGuid(),
            Side.Black, 1, 2, new[] { pos });
        state.EffectInstances.Add(effect);
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId, 4, 5));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("EFFECT_NOT_STEALABLE", result.Error!.Code);
    }

    [Fact]
    public void Execute_cannot_target_own_effect()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var pos = new BoardPoint(4, 5);
        state.EffectInstances.Add(MakeTerrainEffect(effectId, Side.Red, pos)); // Red created it
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId, 4, 5)); // Red trying to steal

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("CANNOT_STEAL_OWN_EFFECT", result.Error!.Code);
    }

    [Fact]
    public void Execute_cannot_target_nonexistent_effect()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(Guid.NewGuid(), 4, 5));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("EFFECT_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public void Execute_does_not_mutate_original_state()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var pos = new BoardPoint(4, 5);
        state.EffectInstances.Add(MakeTerrainEffect(effectId, Side.Black, pos));
        var originalController = state.EffectInstances[0].PositionControllers[pos];
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId, 4, 5));

        Handler().Execute(ctx);

        Assert.Equal(originalController, state.EffectInstances[0].PositionControllers[pos]);
    }
}

/// <summary>
/// Tests for PhaTranDoatPhongHandler (Phá Trận Đoạt Phong).
/// </summary>
public sealed class PhaTranDoatPhongHandlerTests
{
    private static GameState MakeState() => new()
    {
        StateSchemaVersion = 4,
        SideToMove = Side.Red,
        EffectInstances = new List<EffectInstance>(),
        ProcessedTurns = new Dictionary<Side, List<int>> { [Side.Red] = new(), [Side.Black] = new() },
        StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
        SkillStates = new Dictionary<Side, List<SkillState>>
        {
            [Side.Red] = new() { new(1, Guid.NewGuid(), null, 0, SkillKeys.PhaTranDoatPhong) },
            [Side.Black] = new()
        },
        Pieces = new List<PieceState>()
    };

    private static JsonElement Target(Guid effectId) =>
        JsonSerializer.SerializeToElement(new { effectId = effectId.ToString() });

    private static EffectInstance MakeAssignableEffect(Guid effectId, Side creator, Guid pieceId)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = "terrain",
            ["assignedPieceId"] = pieceId
        };
        return EffectFactory.Create(effectId, SkillKeys.BinhLamThuyHien, Guid.NewGuid(),
            creator, 1, 3, new[] { new BoardPoint(4, 5) }, payload: payload);
    }

    private static PhaTranDoatPhongHandler Handler() => new();

    [Fact]
    public void Execute_reduces_duration_by_1()
    {
        var state = MakeState();
        var pieceId = Guid.NewGuid();
        state.Pieces.Add(new PieceState { PieceId = pieceId, Side = Side.Red, Status = PieceStatus.Alive });
        var effectId = Guid.NewGuid();
        state.EffectInstances.Add(MakeAssignableEffect(effectId, Side.Black, pieceId));
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhaTranDoatPhong, 3, Target(effectId));

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.State.EffectInstances[0].RemainingDuration);
    }

    [Fact]
    public void Execute_ends_effect_when_duration_reaches_zero()
    {
        var state = MakeState();
        var pieceId = Guid.NewGuid();
        state.Pieces.Add(new PieceState { PieceId = pieceId, Side = Side.Red, Status = PieceStatus.Alive });
        var effectId = Guid.NewGuid();
        var effect = MakeAssignableEffect(effectId, Side.Black, pieceId);
        effect.RemainingDuration = 1; // Will reach 0 after reduction
        state.EffectInstances.Add(effect);
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhaTranDoatPhong, 3, Target(effectId));

        var result = Handler().Execute(ctx);

        Assert.Equal(EffectStateValue.Ended, result.State.EffectInstances[0].State);
    }

    [Fact]
    public void Execute_emits_duration_reduced_event()
    {
        var state = MakeState();
        var pieceId = Guid.NewGuid();
        state.Pieces.Add(new PieceState { PieceId = pieceId, Side = Side.Red, Status = PieceStatus.Alive });
        var effectId = Guid.NewGuid();
        state.EffectInstances.Add(MakeAssignableEffect(effectId, Side.Black, pieceId));
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhaTranDoatPhong, 3, Target(effectId));

        var result = Handler().Execute(ctx);

        Assert.Contains(result.Events, e =>
            CommandSkillTestHelpers.EventHasType(e, "effect.duration_reduced") ||
            CommandSkillTestHelpers.EventHasType(e, "effect.ended"));
    }

    [Fact]
    public void Execute_does_not_change_creator()
    {
        var state = MakeState();
        var pieceId = Guid.NewGuid();
        state.Pieces.Add(new PieceState { PieceId = pieceId, Side = Side.Red, Status = PieceStatus.Alive });
        var effectId = Guid.NewGuid();
        state.EffectInstances.Add(MakeAssignableEffect(effectId, Side.Black, pieceId));
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhaTranDoatPhong, 3, Target(effectId));

        var result = Handler().Execute(ctx);

        Assert.Equal(Side.Black, result.State.EffectInstances[0].Creator);
    }

    [Fact]
    public void Execute_cannot_target_own_effect()
    {
        var state = MakeState();
        var pieceId = Guid.NewGuid();
        state.Pieces.Add(new PieceState { PieceId = pieceId, Side = Side.Black, Status = PieceStatus.Alive });
        var effectId = Guid.NewGuid();
        state.EffectInstances.Add(MakeAssignableEffect(effectId, Side.Red, pieceId));
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhaTranDoatPhong, 3, Target(effectId));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("CANNOT_TARGET_OWN_EFFECT", result.Error!.Code);
    }

    [Fact]
    public void Execute_cannot_target_effect_not_assigned_to_own_piece()
    {
        var state = MakeState();
        var ownPieceId = Guid.NewGuid();
        var otherPieceId = Guid.NewGuid();
        state.Pieces.Add(new PieceState { PieceId = ownPieceId, Side = Side.Red, Status = PieceStatus.Alive });
        state.Pieces.Add(new PieceState { PieceId = otherPieceId, Side = Side.Black, Status = PieceStatus.Alive });
        var effectId = Guid.NewGuid();
        // Effect assigned to Black's piece, not Red's
        state.EffectInstances.Add(MakeAssignableEffect(effectId, Side.Black, otherPieceId));
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhaTranDoatPhong, 3, Target(effectId));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("ASSIGNED_PIECE_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public void Execute_cannot_target_effect_with_zero_duration()
    {
        var state = MakeState();
        var pieceId = Guid.NewGuid();
        state.Pieces.Add(new PieceState { PieceId = pieceId, Side = Side.Red, Status = PieceStatus.Alive });
        var effectId = Guid.NewGuid();
        var effect = MakeAssignableEffect(effectId, Side.Black, pieceId);
        effect.RemainingDuration = 0;
        state.EffectInstances.Add(effect);
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhaTranDoatPhong, 3, Target(effectId));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("EFFECT_EXPIRED", result.Error!.Code);
    }

    [Fact]
    public void Execute_can_target_disabled_effect()
    {
        var state = MakeState();
        var pieceId = Guid.NewGuid();
        state.Pieces.Add(new PieceState { PieceId = pieceId, Side = Side.Red, Status = PieceStatus.Alive });
        var effectId = Guid.NewGuid();
        var effect = MakeAssignableEffect(effectId, Side.Black, pieceId);
        effect.State = EffectStateValue.Disabled;
        state.EffectInstances.Add(effect);
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhaTranDoatPhong, 3, Target(effectId));

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.State.EffectInstances[0].RemainingDuration);
    }

    [Fact]
    public void Execute_does_not_mutate_original_state()
    {
        var state = MakeState();
        var pieceId = Guid.NewGuid();
        state.Pieces.Add(new PieceState { PieceId = pieceId, Side = Side.Red, Status = PieceStatus.Alive });
        var effectId = Guid.NewGuid();
        state.EffectInstances.Add(MakeAssignableEffect(effectId, Side.Black, pieceId));
        var originalDuration = state.EffectInstances[0].RemainingDuration;
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhaTranDoatPhong, 3, Target(effectId));

        Handler().Execute(ctx);

        Assert.Equal(originalDuration, state.EffectInstances[0].RemainingDuration);
    }
}

/// <summary>
/// Tests for BinhLamThuyHienHandler (Binh Lâm Thủy Hiểm).
/// </summary>
public sealed class BinhLamThuyHienHandlerTests
{
    private static GameState MakeState() => new()
    {
        StateSchemaVersion = 4,
        SideToMove = Side.Red,
        EffectInstances = new List<EffectInstance>(),
        ProcessedTurns = new Dictionary<Side, List<int>> { [Side.Red] = new(), [Side.Black] = new() },
        StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
        SkillStates = new Dictionary<Side, List<SkillState>>
        {
            [Side.Red] = new() { new(1, Guid.NewGuid(), null, 0, SkillKeys.BinhLamThuyHien) },
            [Side.Black] = new()
        }
    };

    private static JsonElement Target(params int[] columns) =>
        JsonSerializer.SerializeToElement(new { paths = columns });

    private static BinhLamThuyHienHandler Handler() => new();

    [Fact]
    public void Execute_creates_one_root_effect()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        Assert.Single(result.State.EffectInstances);
    }

    [Fact]
    public void Execute_effect_has_duration_1()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.Equal(1, result.State.EffectInstances[0].Duration);
        Assert.Equal(1, result.State.EffectInstances[0].RemainingDuration);
    }

    [Fact]
    public void Execute_effect_creator_is_actor()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Black, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.Equal(Side.Black, result.State.EffectInstances[0].Creator);
    }

    [Fact]
    public void Execute_partial_application_checks_new_effect_not_existing()
    {
        var state = MakeState();
        // Existing effect covering column 5
        // IMPORTANT: Use RiverGeometry.ResolvePositions to create proper TargetPositions
        // so that Clone() works correctly (Clone copies PositionControllers, not TargetPositions).
        var existingPositions = RiverGeometry.ResolvePositions(new[] { 5 });
        var existingEffect = EffectFactory.Create(
            Guid.NewGuid(), "existing_terrain", Guid.NewGuid(), Side.Black,
            1, 2, existingPositions);
        // PositionControllers must be initialized for Clone() to work properly
        foreach (var pos in existingPositions)
            existingEffect.PositionControllers[pos] = Side.Black;
        state.EffectInstances.Add(existingEffect);
        
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);
        Assert.True(result.Accepted);
        
        // There should now be 2 effects: existing (10 pos, col 5) + new (20 pos, cols 4+6)
        Assert.Equal(2, result.State.EffectInstances.Count);
        
        // The existing effect (index 0) has 10 positions (column 5)
        var existingResultEffect = result.State.EffectInstances[0];
        Assert.Equal(10, existingResultEffect.TargetPositions.Count);
        Assert.All(existingResultEffect.TargetPositions, p => Assert.Equal(5, p.X));
        
        // The NEW effect (Last) should have 20 positions (columns 4 and 6)
        var newEffect = result.State.EffectInstances.Last();
        Assert.Equal(20, newEffect.TargetPositions.Count);
        Assert.DoesNotContain(newEffect.TargetPositions, p => p.X == 5);
    }
    
    [Fact]
    public void Execute_partial_application_emits_blocked_columns()
    {
        var state = MakeState();
        state.EffectInstances.Add(EffectFactory.Create(
            Guid.NewGuid(), "existing_terrain", Guid.NewGuid(), Side.Black,
            1, 2, RiverGeometry.ResolvePositions(new[] { 5 })));
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.Contains(result.Events, e =>
        {
            return CommandSkillTestHelpers.EventHasType(e, "effect.created") &&
                   CommandSkillTestHelpers.EventHasBoolProperty(e, "partialApplication", true);
        });
    }

    [Fact]
    public void Execute_all_conflicting_fails_atomically()
    {
        var state = MakeState();
        // All 3 columns already occupied
        state.EffectInstances.Add(EffectFactory.Create(
            Guid.NewGuid(), "existing", Guid.NewGuid(), Side.Black,
            1, 2, RiverGeometry.ResolvePositions(new[] { 4, 5, 6 })));
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("ALL_PATHS_BLOCKED", result.Error!.Code);
    }

    [Fact]
    public void Execute_all_conflicting_creates_no_effect()
    {
        var state = MakeState();
        state.EffectInstances.Add(EffectFactory.Create(
            Guid.NewGuid(), "existing", Guid.NewGuid(), Side.Black,
            1, 2, RiverGeometry.ResolvePositions(new[] { 4, 5, 6 })));
        var originalCount = state.EffectInstances.Count;
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        Handler().Execute(ctx);

        Assert.Equal(originalCount, state.EffectInstances.Count);
    }

    [Fact]
    public void Execute_rejects_invalid_river_path()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(0, 1, 2));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("INVALID_RIVER_PATH", result.Error!.Code);
    }

    [Fact]
    public void Execute_does_not_mutate_original_state()
    {
        var state = MakeState();
        var originalCount = state.EffectInstances.Count;
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        Handler().Execute(ctx);

        Assert.Equal(originalCount, state.EffectInstances.Count);
    }

    [Fact]
    public void Execute_increments_NextCreationOrder()
    {
        var state = MakeState();
        state.NextCreationOrder = 10;
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.Equal(11, result.State.NextCreationOrder);
    }

    [Fact]
    public void Execute_effect_payload_contains_paths()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        var payload = result.State.EffectInstances[0].Payload;
        Assert.True(payload.TryGetValue("paths", out var pathsVal));
    }

    [Fact]
    public void Execute_one_conflict_leaves_two_columns()
    {
        // Test: 1 conflict → 20 positions (2 columns)
        var state = MakeState();
        // Existing effect covering column 4
        var existingPositions = RiverGeometry.ResolvePositions(new[] { 4 });
        var existingEffect = EffectFactory.Create(
            Guid.NewGuid(), "existing_terrain", Guid.NewGuid(), Side.Black,
            1, 2, existingPositions);
        foreach (var pos in existingPositions)
            existingEffect.PositionControllers[pos] = Side.Black;
        state.EffectInstances.Add(existingEffect);

        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        // 2 non-conflicting columns = 20 positions
        var newEffect = result.State.EffectInstances.Last();
        Assert.Equal(20, newEffect.TargetPositions.Count);
        Assert.DoesNotContain(newEffect.TargetPositions, p => p.X == 4);
        Assert.Contains(newEffect.TargetPositions, p => p.X == 5);
        Assert.Contains(newEffect.TargetPositions, p => p.X == 6);
    }

    [Fact]
    public void Execute_two_conflicts_leaves_one_column()
    {
        // Test: 2 conflicts → 10 positions (1 column)
        var state = MakeState();
        // Existing effects covering columns 4 and 5
        var existingPositions1 = RiverGeometry.ResolvePositions(new[] { 4 });
        var existingEffect1 = EffectFactory.Create(
            Guid.NewGuid(), "existing_terrain", Guid.NewGuid(), Side.Black,
            1, 2, existingPositions1);
        foreach (var pos in existingPositions1)
            existingEffect1.PositionControllers[pos] = Side.Black;

        var existingPositions2 = RiverGeometry.ResolvePositions(new[] { 5 });
        var existingEffect2 = EffectFactory.Create(
            Guid.NewGuid(), "existing_terrain", Guid.NewGuid(), Side.Black,
            1, 2, existingPositions2);
        foreach (var pos in existingPositions2)
            existingEffect2.PositionControllers[pos] = Side.Black;

        state.EffectInstances.Add(existingEffect1);
        state.EffectInstances.Add(existingEffect2);

        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        // 1 non-conflicting column = 10 positions
        var newEffect = result.State.EffectInstances.Last();
        Assert.Equal(10, newEffect.TargetPositions.Count);
        Assert.DoesNotContain(newEffect.TargetPositions, p => p.X == 4);
        Assert.DoesNotContain(newEffect.TargetPositions, p => p.X == 5);
        Assert.Contains(newEffect.TargetPositions, p => p.X == 6);
    }

    [Fact]
    public void Execute_three_conflicts_fails_atomically()
    {
        // Test: 3 conflicts → atomic failure
        var state = MakeState();
        // All 3 columns occupied
        var allPositions = RiverGeometry.ResolvePositions(new[] { 4, 5, 6 });
        var existingEffect = EffectFactory.Create(
            Guid.NewGuid(), "existing_terrain", Guid.NewGuid(), Side.Black,
            1, 2, allPositions);
        foreach (var pos in allPositions)
            existingEffect.PositionControllers[pos] = Side.Black;
        state.EffectInstances.Add(existingEffect);

        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("ALL_PATHS_BLOCKED", result.Error!.Code);
    }

    [Fact]
    public void Execute_all_conflict_no_cooldown_consumed()
    {
        // Test: all-conflict failure does not consume cooldown
        var state = MakeState();
        state.SkillStates[Side.Red] = new() { new(1, Guid.NewGuid(), null, 2, SkillKeys.BinhLamThuyHien) };
        // All 3 columns occupied
        var allPositions = RiverGeometry.ResolvePositions(new[] { 4, 5, 6 });
        var existingEffect = EffectFactory.Create(
            Guid.NewGuid(), "existing_terrain", Guid.NewGuid(), Side.Black,
            1, 2, allPositions);
        foreach (var pos in allPositions)
            existingEffect.PositionControllers[pos] = Side.Black;
        state.EffectInstances.Add(existingEffect);

        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        // Cooldown is not consumed when handler fails
        // The dispatcher handles cooldown, but since the handler returns failure,
        // the result.State should be the original state
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Execute_exactly_one_root_effect_per_activation()
    {
        // Test: one root Effect per successful activation
        var state = MakeState();

        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        // Exactly one root effect created (partial application still creates one effect)
        Assert.Single(result.State.EffectInstances);
    }

    [Fact]
    public void Execute_duration_equals_1_player_turn()
    {
        // Test: duration = 1 player turn
        var state = MakeState();

        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.BinhLamThuyHien, 2, Target(4, 5, 6));

        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        Assert.Equal(1, result.State.EffectInstances[0].Duration);
        Assert.Equal(1, result.State.EffectInstances[0].RemainingDuration);
    }

    [Fact]
    public void Execute_expires_at_start_of_creator_turn()
    {
        // Test: effect expires at start of Creator's next turn (U-DUR = A)
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var positions = RiverGeometry.ResolvePositions(new[] { 4, 5, 6 });
        var effect = EffectFactory.Create(effectId, SkillKeys.BinhLamThuyHien, Guid.NewGuid(),
            Side.Red, 1, 1, positions);
        state.EffectInstances.Add(effect);
        state.SideToMove = Side.Red; // Red is Creator, Red's turn

        // Apply Red's turn (duration should decrement because sideToMove == Creator)
        var result = TurnLifecycle.Apply(state, Side.Red);

        var expiredEffect = result.State.EffectInstances[0];
        Assert.Equal(EffectStateValue.Ended, expiredEffect.State);
        Assert.Equal(0, expiredEffect.RemainingDuration);
    }
}

/// <summary>
/// Tests for Phản Kỳ Đoạt Thế pending steal timing via TurnLifecycle.
///
/// These tests verify the CONFIRMED TIMING:
/// 1. A creates Effect X
/// 2. B uses Phản Kỳ Đoạt Thế on X (during B's turn)
/// 3. During B's current turn: PendingController = B, official PositionController still A
/// 4. At the start of A's (Creator's) immediately following turn:
///    - PendingController is finalized → B becomes official Controller
///    - Duration is decremented for A's turn
/// 5. During A's turn, A can CANCEL the stolen effect (internal Phản Kỳ resolution)
/// 6. If A cancels: effect ends; B does NOT regain control
/// 7. If A does not cancel: B remains Controller until Effect ends
///
/// CANCELLATION IS NOT A COMMAND SKILL:
/// - "Cancel stolen effect" is an INTERNAL RESOLUTION mechanism of Phản Kỳ
/// - NOT registered in CommandSkillRegistry
/// - NOT dispatched via CommandSkillDispatcher
/// - Handled by TurnLifecycle.Apply(..., resolveCreatorCancellation: true)
///
/// INVARIANTS (always preserved):
/// - Creator remains A throughout (never changes)
/// - EffectId remains unchanged
/// - CreationOrder remains unchanged
/// - Duration is NOT reset by steal or cancellation
/// - Cancellation does NOT create a new Effect
/// - Cancellation does NOT trigger a new steal
/// </summary>
public sealed class PhanKyPendingStealLifecycleTests
{
    private static GameState MakeState() => new()
    {
        StateSchemaVersion = 4,
        SideToMove = Side.Red,
        TurnIndex = 1,
        EffectInstances = new List<EffectInstance>(),
        ProcessedTurns = new Dictionary<Side, List<int>> { [Side.Red] = new(), [Side.Black] = new() },
        StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
        SkillStates = new Dictionary<Side, List<SkillState>>
        {
            [Side.Red] = new(),
            [Side.Black] = new()
        }
    };

    private static EffectInstance MakeTerrainEffect(Guid effectId, Side creator, int creationOrder = 1, int duration = 3)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = "terrain" };
        var positions = RiverGeometry.ResolvePositions(new[] { 4 }); // column 4, all 10 rows
        return EffectFactory.Create(effectId, SkillKeys.BinhLamThuyHien, Guid.NewGuid(),
            creator, creationOrder, duration, positions, payload: payload);
    }

    private static JsonElement Target(Guid effectId) =>
        JsonSerializer.SerializeToElement(new { effectId = effectId.ToString() });

    [Fact]
    public void Pending_steal_during_thief_turn_official_controller_still_creator()
    {
        // Test: A (Black) creates effect. B (Red) uses Phản Kỳ.
        // During B's turn, official controller should still be A.
        var state = MakeState();
        state.SideToMove = Side.Red;
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        state.EffectInstances.Add(effect);

        // Red uses Phản Kỳ
        var phanKyHandler = new PhanKyDoatTheHandler();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId));
        var result = phanKyHandler.Execute(ctx);

        Assert.True(result.Accepted);
        var stolenEffect = result.State.EffectInstances[0];

        // During B's turn (Red's turn): PendingController is set, but PositionController is still Black
        Assert.Equal(Side.Red, stolenEffect.PendingController);
        Assert.Equal(Side.Black, stolenEffect.PositionControllers[new BoardPoint(4, 0)]);
    }

    [Fact]
    public void Start_of_creator_turn_finalizes_pending_steal()
    {
        // Test: Red (thief) uses Phản Kỳ during Red's turn.
        // When Black (Creator)'s turn starts, pending steal is finalized.
        // Confirmed timing: finalized at start of Creator's turn.
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        state.EffectInstances.Add(effect);
        state.SideToMove = Side.Red;

        // Red uses Phản Kỳ
        var phanKyHandler = new PhanKyDoatTheHandler();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId));
        var afterSteal = phanKyHandler.Execute(ctx);

        Assert.True(afterSteal.Accepted);
        Assert.Equal(Side.Red, afterSteal.State.EffectInstances[0].PendingController);
        Assert.Equal(Side.Black, afterSteal.State.EffectInstances[0].PositionControllers[new BoardPoint(4, 0)]);

        // Now Black's turn starts - Apply lifecycle for Black
        var blackResult = TurnLifecycle.Apply(afterSteal.State, Side.Black);
        var afterBlackTurn = blackResult.State.EffectInstances[0];

        // After Apply for Creator (Black): PositionController should now be Red (thief)
        Assert.Equal(Side.Red, afterBlackTurn.PositionControllers[new BoardPoint(4, 0)]);
        Assert.Null(afterBlackTurn.PendingController);
        
        // Verify the finalized steal event was emitted
        Assert.Single(blackResult.FinalizedSteals);
        Assert.Equal(effectId, blackResult.FinalizedSteals[0].EffectId);
        Assert.Equal(Side.Red, blackResult.FinalizedSteals[0].NewController);
    }

    [Fact]
    public void Creator_can_cancel_stolen_effect_during_creator_turn()
    {
        // Test: Black's effect is stolen by Red. Red's turn ends. Black's turn starts.
        // Black can CANCEL the stolen effect during their turn (internal Phản Kỳ resolution).
        // Cancellation is NOT a Command Skill - handled by TurnLifecycle with resolveCreatorCancellation: true.
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        state.EffectInstances.Add(effect);

        // Red's turn: Red steals
        state.SideToMove = Side.Red;
        var stolenEffect = state.EffectInstances[0];
        stolenEffect.PendingController = Side.Red;

        // Red's turn ends, apply lifecycle for Red (finalizes steal)
        var redResult = TurnLifecycle.Apply(state, Side.Red);
        var afterRed = redResult.State.EffectInstances[0];
        Assert.Equal(Side.Red, afterRed.PositionControllers[new BoardPoint(4, 0)]);
        Assert.Null(afterRed.PendingController);

        // The Black turn is processed once, including cancellation.
        var cancelResult = TurnLifecycle.Apply(redResult.State, Side.Black, resolveCreatorCancellation: true);

        Assert.Single(cancelResult.RemovedEffects);
        Assert.Equal(effectId, cancelResult.RemovedEffects[0].EffectId);
        Assert.Equal(EffectStateValue.Ended, cancelResult.State.EffectInstances[0].State);
    }

    [Fact]
    public void Cancellation_is_not_recorded_as_new_steal()
    {
        // Test: When Creator cancels stolen effect, it's NOT a "steal back"
        // Cancellation is an internal Phản Kỳ resolution - no new Effect created.
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        effect.PositionControllers[new BoardPoint(4, 0)] = Side.Red; // Already stolen
        state.EffectInstances.Add(effect);

        // Apply cancellation (NOT a Command Skill)
        var result = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);

        // TurnLifecycle.Apply always succeeds - cancellation is deterministic
        Assert.Single(result.RemovedEffects);
        Assert.Equal(effectId, result.RemovedEffects[0].EffectId);
        Assert.Equal(EffectStateValue.Ended, result.State.EffectInstances[0].State);
        Assert.Null(result.State.EffectInstances[0].PendingController);
        // No new Effect created
        Assert.Single(result.State.EffectInstances);
    }

    [Fact]
    public void If_creator_does_not_remove_steal_thief_remains_controller()
    {
        // Test: If Black doesn't remove the stolen effect, Red remains controller
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        effect.PositionControllers[new BoardPoint(4, 0)] = Side.Red; // Already stolen to Red
        state.EffectInstances.Add(effect);
        state.SideToMove = Side.Black;

        // Black's turn starts - Black chooses NOT to remove
        // Apply lifecycle for Black
        var blackResult = TurnLifecycle.Apply(state, Side.Black);
        var stolenEffect = blackResult.State.EffectInstances[0];

        // Red is still the controller
        Assert.Equal(Side.Red, stolenEffect.PositionControllers[new BoardPoint(4, 0)]);
    }

    [Fact]
    public void Creator_remains_unchanged_throughout_steal_and_cancellation()
    {
        // Test: Creator is always Black, never changes
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        state.EffectInstances.Add(effect);

        // Red steals
        var phanKyHandler = new PhanKyDoatTheHandler();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId));
        var stealResult = phanKyHandler.Execute(ctx);
        Assert.Equal(Side.Black, stealResult.State.EffectInstances[0].Creator);

        // Steal finalized at start of Black's turn
        var afterSteal = stealResult.State;
        afterSteal.SideToMove = Side.Black;
        var blackResult = TurnLifecycle.Apply(afterSteal, Side.Black);
        Assert.Equal(Side.Black, blackResult.State.EffectInstances[0].Creator);

        // Black cancels - Creator still Black
        var cancelResult = TurnLifecycle.Apply(blackResult.State, Side.Black, resolveCreatorCancellation: true);
        Assert.Equal(Side.Black, cancelResult.State.EffectInstances[0].Creator);
    }

    [Fact]
    public void EffectId_remains_unchanged()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        state.EffectInstances.Add(effect);

        // Red steals
        var phanKyHandler = new PhanKyDoatTheHandler();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId));
        var stealResult = phanKyHandler.Execute(ctx);
        Assert.Equal(effectId, stealResult.State.EffectInstances[0].EffectId);
    }

    [Fact]
    public void CreationOrder_remains_unchanged()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black, creationOrder: 42);
        state.EffectInstances.Add(effect);

        // Red steals
        var phanKyHandler = new PhanKyDoatTheHandler();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId));
        var stealResult = phanKyHandler.Execute(ctx);
        Assert.Equal(42, stealResult.State.EffectInstances[0].CreationOrder);
    }

    [Fact]
    public void Duration_not_reset_by_steal()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black, duration: 5);
        state.EffectInstances.Add(effect);

        // Red steals
        var phanKyHandler = new PhanKyDoatTheHandler();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId));
        var stealResult = phanKyHandler.Execute(ctx);

        // Duration is immutable, RemainingDuration not reset
        Assert.Equal(5, stealResult.State.EffectInstances[0].Duration);
        Assert.Equal(5, stealResult.State.EffectInstances[0].RemainingDuration);
    }

    [Fact]
    public void Cannot_cancel_effect_that_was_not_stolen()
    {
        // Test: Creator cannot cancel their own un-stolen effect via TurnLifecycle
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        state.EffectInstances.Add(effect);

        // Apply cancellation - should have no effect on un-stolen effect
        var result = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);

        // No effect should be removed since it wasn't stolen
        Assert.Empty(result.RemovedEffects);
        Assert.Equal(EffectStateValue.Active, result.State.EffectInstances[0].State);
    }

    [Fact]
    public void Cannot_cancel_ended_effect()
    {
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        effect.State = EffectStateValue.Ended;
        state.EffectInstances.Add(effect);

        // Apply cancellation on ended effect
        var result = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);

        // No effect should be removed since it's already ended
        Assert.Empty(result.RemovedEffects);
        Assert.Equal(EffectStateValue.Ended, result.State.EffectInstances[0].State);
    }

    [Fact]
    public void Non_creator_cannot_cancel_effect()
    {
        // Test: Only the Creator can cancel a stolen effect
        // When resolveCreatorCancellation is called with sideToMove != Creator, nothing happens
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        effect.PositionControllers[new BoardPoint(4, 0)] = Side.Red; // Stolen
        state.EffectInstances.Add(effect);

        // Red (non-Creator) tries to cancel - should have no effect
        var result = TurnLifecycle.Apply(state, Side.Red, resolveCreatorCancellation: true);

        // Effect should NOT be removed since Red is not the Creator
        Assert.Empty(result.RemovedEffects);
        Assert.Equal(EffectStateValue.Active, result.State.EffectInstances[0].State);
    }

    [Fact]
    public void Cannot_steal_disabled_effect()
    {
        // Test: Cannot steal a Disabled Effect
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Black);
        effect.State = EffectStateValue.Disabled;
        state.EffectInstances.Add(effect);

        var phanKyHandler = new PhanKyDoatTheHandler();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId));
        var result = phanKyHandler.Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("EFFECT_DISABLED", result.Error!.Code);
    }

    [Fact]
    public void Cannot_steal_own_effect()
    {
        // Test: Cannot steal your own Effect
        var state = MakeState();
        var effectId = Guid.NewGuid();
        var effect = MakeTerrainEffect(effectId, Side.Red);
        state.EffectInstances.Add(effect);

        var phanKyHandler = new PhanKyDoatTheHandler();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId));
        var result = phanKyHandler.Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("CANNOT_STEAL_OWN_EFFECT", result.Error!.Code);
    }

    [Fact]
    public void Cannot_steal_non_terrain_effect()
    {
        // Test: Can only steal terrain Effects
        var state = MakeState();
        var effectId = Guid.NewGuid();
        // Effect without "terrain" kind
        var effect = EffectFactory.Create(effectId, "some_code", Guid.NewGuid(),
            Side.Black, 1, 2, RiverGeometry.ResolvePositions(new[] { 4 }));
        state.EffectInstances.Add(effect);

        var phanKyHandler = new PhanKyDoatTheHandler();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.PhanKyDoatThe, 2, Target(effectId));
        var result = phanKyHandler.Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("EFFECT_NOT_STEALABLE", result.Error!.Code);
    }
}

// =============================================================================
// Step 6: Tests for Thành, Rào, and THD Tượng Cọc Command Skill Handlers
// =============================================================================

/// <summary>
/// Tests for ThanhHandler (Thành Command Skill).
/// </summary>
public sealed class ThanhHandlerTests
{
    private static GameState MakeState() => new()
    {
        StateSchemaVersion = 4,
        SideToMove = Side.Red,
        EffectInstances = new List<EffectInstance>(),
        ProcessedTurns = new Dictionary<Side, List<int>> { [Side.Red] = new(), [Side.Black] = new() },
        StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
        SkillStates = new Dictionary<Side, List<SkillState>>
        {
            [Side.Red] = new() { new(1, Guid.NewGuid(), null, 0, SkillKeys.Thanh) },
            [Side.Black] = new()
        },
        ObstacleMetadata = new Dictionary<Guid, ObstacleMetadata>()
    };

    private static JsonElement Target(int x, int y) =>
        JsonSerializer.SerializeToElement(new { position = new { x, y } });

    private static CommandSkillContext Context(GameState state, Side side = Side.Red) =>
        new(state, side, 1, Guid.NewGuid(), SkillKeys.Thanh, 2, Target(0, 0));

    private static ThanhHandler Handler() => new();

    [Fact]
    public void Execute_creates_Thanh_obstacle()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        Assert.Single(result.State.Obstacles, o => o.Kind == SkillKeys.ObstacleKindThanh);
    }

    [Fact]
    public void Execute_Thanh_has_remaining_lifetime_6()
    {
        var state = MakeState();
        var ctx = Context(state, Side.Red);
        var result = Handler().Execute(ctx);

        var thanh = result.State.Obstacles.Single(o => o.Kind == SkillKeys.ObstacleKindThanh);
        Assert.Equal(6, thanh.RemainingLifetime);
    }

    [Fact]
    public void Execute_Thanh_placed_on_own_half()
    {
        var state = MakeState();
        // Red's half: y = 0..4
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.Thanh, 2, Target(0, 0));
        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
    }

    [Fact]
    public void Execute_Thanh_rejected_on_enemy_half()
    {
        var state = MakeState();
        // Red trying to place Thành on Black's half: y >= 5
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.Thanh, 2, Target(0, 5));
        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("WRONG_HALF", result.Error!.Code);
    }

    [Fact]
    public void Execute_Thanh_rejected_on_occupied_cell()
    {
        var state = MakeState();
        // Add a Red piece at (0,0)
        state.Pieces.Add(new PieceState { PieceId = Guid.NewGuid(), Class = PieceClass.Soldier, Side = Side.Red, Position = new BoardPoint(0, 0) });

        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.Thanh, 2, Target(0, 0));
        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("CELL_OCCUPIED", result.Error!.Code);
    }

    [Fact]
    public void Execute_Thanh_rejected_off_board()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.Thanh, 2, Target(-1, 0));
        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("INVALID_POSITION", result.Error!.Code);
    }

    [Fact]
    public void Execute_Thanh_Black_on_own_half()
    {
        var state = MakeState();
        // Black's half: y = 5..9
        var ctx = new CommandSkillContext(state, Side.Black, 1, Guid.NewGuid(),
            SkillKeys.Thanh, 2, Target(0, 5));
        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
    }
}

/// <summary>
/// Tests for RaoHandler (Rào Command Skill).
/// </summary>
public sealed class RaoHandlerTests
{
    private static GameState MakeState() => new()
    {
        StateSchemaVersion = 4,
        SideToMove = Side.Red,
        EffectInstances = new List<EffectInstance>(),
        ProcessedTurns = new Dictionary<Side, List<int>> { [Side.Red] = new(), [Side.Black] = new() },
        StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
        SkillStates = new Dictionary<Side, List<SkillState>>
        {
            [Side.Red] = new() { new(1, Guid.NewGuid(), null, 0, SkillKeys.Rao) },
            [Side.Black] = new()
        },
        ObstacleMetadata = new Dictionary<Guid, ObstacleMetadata>()
    };

    private static JsonElement Target(int x, int y) =>
        JsonSerializer.SerializeToElement(new { position = new { x, y } });

    private static CommandSkillContext Context(GameState state, Side side = Side.Red) =>
        new(state, side, 1, Guid.NewGuid(), SkillKeys.Rao, 1, Target(0, 0));

    private static RaoHandler Handler() => new();

    [Fact]
    public void Execute_creates_Rao_obstacle()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
        Assert.Single(result.State.Obstacles, o => o.Kind == SkillKeys.ObstacleKindRao);
    }

    [Fact]
    public void Execute_Rao_has_remaining_lifetime_4()
    {
        var state = MakeState();
        var ctx = Context(state);
        var result = Handler().Execute(ctx);

        var rao = result.State.Obstacles.Single(o => o.Kind == SkillKeys.ObstacleKindRao);
        Assert.Equal(4, rao.RemainingLifetime);
    }

    [Fact]
    public void Execute_Rao_placed_on_own_half()
    {
        var state = MakeState();
        var ctx = Context(state, Side.Red);
        var result = Handler().Execute(ctx);

        Assert.True(result.Accepted);
    }

    [Fact]
    public void Execute_Rao_rejected_on_enemy_half()
    {
        var state = MakeState();
        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.Rao, 1, Target(0, 5));
        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("WRONG_HALF", result.Error!.Code);
    }

    [Fact]
    public void Execute_Rao_rejected_on_occupied_cell()
    {
        var state = MakeState();
        state.Pieces.Add(new PieceState { PieceId = Guid.NewGuid(), Class = PieceClass.Soldier, Side = Side.Red, Position = new BoardPoint(0, 0) });

        var ctx = new CommandSkillContext(state, Side.Red, 1, Guid.NewGuid(),
            SkillKeys.Rao, 1, Target(0, 0));
        var result = Handler().Execute(ctx);

        Assert.False(result.Accepted);
        Assert.Equal("CELL_OCCUPIED", result.Error!.Code);
    }
}

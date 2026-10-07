// Phase 2.1: Effect domain model unit tests.
using HeroChess.Rules;
using HeroChess.Rules.Effects;
using Xunit;

namespace HeroChess.Rules.Tests;

/// <summary>
/// Unit tests for the Effect domain model (§6 of the implementation plan).
/// Tests cover: EffectInstance invariants, EffectFactory, EffectQueries, StateSchemaUpgrade.
/// </summary>
public sealed class EffectModelTests
{
    #region EffectInstance construction and invariants

    [Fact]
    public void EffectInstance_Clone_produces_independent_copy()
    {
        // Arrange
        var original = new EffectInstance
        {
            EffectId = Guid.NewGuid(),
            Code = "test_effect",
            SkillId = Guid.NewGuid(),
            Creator = Side.Red,
            CreationOrder = 5,
            State = EffectStateValue.Active,
            Duration = 3,
            RemainingDuration = 3,
            TargetPositions = new List<BoardPoint> { new(4, 0), new(4, 1) },
            PositionControllers = new Dictionary<BoardPoint, Side> { [new(4, 0)] = Side.Red, [new(4, 1)] = Side.Red }
        };

        // Act
        var clone = original.Clone();

        // Assert — fields copied
        Assert.Equal(original.EffectId, clone.EffectId);
        Assert.Equal(original.Code, clone.Code);
        Assert.Equal(original.Creator, clone.Creator);
        Assert.Equal(original.CreationOrder, clone.CreationOrder);
        Assert.Equal(original.Duration, clone.Duration);
        Assert.Equal(original.RemainingDuration, clone.RemainingDuration);
        Assert.Equal(original.State, clone.State);

        // Assert — collections are independent (no shared references)
        Assert.NotSame(original.TargetPositions, clone.TargetPositions);
        Assert.NotSame(original.PositionControllers, clone.PositionControllers);
        Assert.Equal(original.TargetPositions.Count, clone.TargetPositions.Count);

        // Mutating the clone does not affect the original
        clone.RemainingDuration = 0;
        clone.State = EffectStateValue.Ended;
        Assert.Equal(3, original.RemainingDuration);
        Assert.Equal(EffectStateValue.Active, original.State);
    }

    [Fact]
    public void EffectInstance_Clone_preserves_PositionControllers_contents()
    {
        var pos1 = new BoardPoint(4, 0);
        var pos2 = new BoardPoint(4, 1);
        var original = new EffectInstance
        {
            EffectId = Guid.NewGuid(),
            Code = "test",
            SkillId = Guid.NewGuid(),
            Creator = Side.Black,
            CreationOrder = 1,
            Duration = 2,
            RemainingDuration = 2,
            State = EffectStateValue.Active,
            TargetPositions = new List<BoardPoint> { pos1, pos2 },
            PositionControllers = new Dictionary<BoardPoint, Side>
            {
                [pos1] = Side.Black,
                [pos2] = Side.Red  // split
            }
        };

        var clone = original.Clone();

        Assert.Equal(2, clone.PositionControllers.Count);
        Assert.Equal(Side.Black, clone.PositionControllers[pos1]);
        Assert.Equal(Side.Red, clone.PositionControllers[pos2]);
    }

    #endregion

    #region EffectFactory

    [Fact]
    public void EffectFactory_Create_initializes_RemainingDuration_to_Duration()
    {
        var effect = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: "test_effect",
            skillId: Guid.NewGuid(),
            creator: Side.Red,
            creationOrder: 1,
            duration: 3,
            targetPositions: new List<BoardPoint> { new(4, 0) }
        );

        Assert.Equal(3, effect.Duration);
        Assert.Equal(3, effect.RemainingDuration);
    }

    [Fact]
    public void EffectFactory_Create_sets_State_to_Active()
    {
        var effect = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: "test_effect",
            skillId: Guid.NewGuid(),
            creator: Side.Red,
            creationOrder: 1,
            duration: 2,
            targetPositions: new List<BoardPoint> { new(4, 0) }
        );

        Assert.Equal(EffectStateValue.Active, effect.State);
    }

    [Fact]
    public void EffectFactory_Create_initializes_PositionControllers_for_all_target_positions()
    {
        var pos1 = new BoardPoint(4, 0);
        var pos2 = new BoardPoint(4, 1);
        var pos3 = new BoardPoint(4, 2);
        var positions = new List<BoardPoint> { pos1, pos2, pos3 };

        var effect = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: "test_effect",
            skillId: Guid.NewGuid(),
            creator: Side.Red,
            creationOrder: 1,
            duration: 1,
            targetPositions: positions
        );

        Assert.Equal(3, effect.PositionControllers.Count);
        Assert.Equal(Side.Red, effect.PositionControllers[pos1]);
        Assert.Equal(Side.Red, effect.PositionControllers[pos2]);
        Assert.Equal(Side.Red, effect.PositionControllers[pos3]);
    }

    [Fact]
    public void EffectFactory_Create_respects_initialControllers_parameter()
    {
        var pos1 = new BoardPoint(4, 0);
        var pos2 = new BoardPoint(4, 1);
        var positions = new List<BoardPoint> { pos1, pos2 };
        var initialControllers = new Dictionary<BoardPoint, Side>
        {
            [pos1] = Side.Red,
            [pos2] = Side.Black  // pre-split
        };

        var effect = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: "test_effect",
            skillId: Guid.NewGuid(),
            creator: Side.Red,
            creationOrder: 1,
            duration: 1,
            targetPositions: positions,
            initialControllers: initialControllers
        );

        Assert.Equal(Side.Red, effect.PositionControllers[pos1]);
        Assert.Equal(Side.Black, effect.PositionControllers[pos2]);
    }

    [Fact]
    public void EffectFactory_Create_uses_Creator_for_unspecified_positions()
    {
        var pos1 = new BoardPoint(4, 0);
        var pos2 = new BoardPoint(4, 1);
        var positions = new List<BoardPoint> { pos1, pos2 };
        var initialControllers = new Dictionary<BoardPoint, Side> { [pos1] = Side.Red };

        var effect = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: "test_effect",
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 1,
            targetPositions: positions,
            initialControllers: initialControllers
        );

        Assert.Equal(Side.Red, effect.PositionControllers[pos1]);
        Assert.Equal(Side.Black, effect.PositionControllers[pos2]); // falls back to creator
    }

    [Fact]
    public void EffectFactory_Create_stores_Payload()
    {
        var payload = new Dictionary<string, object?> { ["key"] = "value", ["count"] = 42 };
        var effect = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: "test_effect",
            skillId: Guid.NewGuid(),
            creator: Side.Red,
            creationOrder: 1,
            duration: 2,
            targetPositions: new List<BoardPoint> { new(4, 0) },
            payload: payload
        );

        Assert.Equal("value", effect.Payload["key"]);
        Assert.Equal(42, effect.Payload["count"]);
    }

    [Fact]
    public void EffectFactory_Clone_preserves_all_fields()
    {
        var pos = new BoardPoint(4, 0);
        var original = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: "test_effect",
            skillId: Guid.NewGuid(),
            creator: Side.Red,
            creationOrder: 3,
            duration: 2,
            targetPositions: new List<BoardPoint> { pos }
        );
        original.State = EffectStateValue.Disabled;
        original.RemainingDuration = 1;

        var clone = EffectFactory.Clone(original);

        Assert.Equal(original.EffectId, clone.EffectId);
        Assert.Equal(original.Code, clone.Code);
        Assert.Equal(original.Creator, clone.Creator);
        Assert.Equal(original.CreationOrder, clone.CreationOrder);
        Assert.Equal(original.Duration, clone.Duration);
        Assert.Equal(original.RemainingDuration, clone.RemainingDuration);
        Assert.Equal(original.State, clone.State);
        Assert.NotSame(original.PositionControllers, clone.PositionControllers);
    }

    #endregion

    #region EffectQueries

    [Fact]
    public void EffectQueries_ActiveEffects_returns_only_active()
    {
        var state = EmptyState();
        var active = MakeEffect(Side.Red, EffectStateValue.Active);
        var disabled = MakeEffect(Side.Red, EffectStateValue.Disabled);
        var ended = MakeEffect(Side.Red, EffectStateValue.Ended);
        state.EffectInstances.AddRange(new[] { active, disabled, ended });

        var result = state.ActiveEffects().ToList();

        Assert.Single(result);
        Assert.Same(active, result[0]);
    }

    [Fact]
    public void EffectQueries_NonEndedEffects_excludes_ended()
    {
        var state = EmptyState();
        var active = MakeEffect(Side.Red, EffectStateValue.Active);
        var disabled = MakeEffect(Side.Red, EffectStateValue.Disabled);
        var ended = MakeEffect(Side.Red, EffectStateValue.Ended);
        state.EffectInstances.AddRange(new[] { active, disabled, ended });

        var result = state.NonEndedEffects().ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(active, result);
        Assert.Contains(disabled, result);
        Assert.DoesNotContain(ended, result);
    }

    [Fact]
    public void EffectQueries_FindEffect_returns_matching_effect()
    {
        var state = EmptyState();
        var targetEffectId = Guid.NewGuid();
        var target = MakeEffect(Side.Red, EffectStateValue.Active, effectId: targetEffectId);
        state.EffectInstances.Add(target);
        state.EffectInstances.Add(MakeEffect(Side.Red, EffectStateValue.Active));

        var result = state.FindEffect(targetEffectId);

        Assert.Same(target, result);
    }

    [Fact]
    public void EffectQueries_FindEffect_returns_null_when_not_found()
    {
        var state = EmptyState();
        state.EffectInstances.Add(MakeEffect(Side.Red, EffectStateValue.Active));

        var result = state.FindEffect(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void EffectQueries_TryGetEffect_returns_true_when_found()
    {
        var state = EmptyState();
        var targetEffectId = Guid.NewGuid();
        var target = MakeEffect(Side.Red, EffectStateValue.Active, effectId: targetEffectId);
        state.EffectInstances.Add(target);

        var found = state.TryGetEffect(targetEffectId, out var effect);

        Assert.True(found);
        Assert.Same(target, effect);
    }

    [Fact]
    public void EffectQueries_TryGetEffect_returns_false_when_not_found()
    {
        var state = EmptyState();
        state.EffectInstances.Add(MakeEffect(Side.Red, EffectStateValue.Active));

        var found = state.TryGetEffect(Guid.NewGuid(), out var effect);

        Assert.False(found);
        Assert.Null(effect);
    }

    [Fact]
    public void EffectQueries_EffectsByCreator_filters_correctly()
    {
        var state = EmptyState();
        var redEffect = MakeEffect(Side.Red, EffectStateValue.Active);
        var blackEffect = MakeEffect(Side.Black, EffectStateValue.Active);
        state.EffectInstances.AddRange(new[] { redEffect, blackEffect });

        var redEffects = state.EffectsByCreator(Side.Red).ToList();
        var blackEffects = state.EffectsByCreator(Side.Black).ToList();

        Assert.Single(redEffects);
        Assert.Same(redEffect, redEffects[0]);
        Assert.Single(blackEffects);
        Assert.Same(blackEffect, blackEffects[0]);
    }

    [Fact]
    public void EffectQueries_ActiveEffectsByCreator_combines_filters()
    {
        var state = EmptyState();
        var redActive = MakeEffect(Side.Red, EffectStateValue.Active);
        var redEnded = MakeEffect(Side.Red, EffectStateValue.Ended);
        state.EffectInstances.AddRange(new[] { redActive, redEnded });

        var result = state.ActiveEffectsByCreator(Side.Red).ToList();

        Assert.Single(result);
        Assert.Same(redActive, result[0]);
    }

    [Fact]
    public void EffectQueries_EffectsAtPosition_filters_by_board_point()
    {
        var state = EmptyState();
        var p1 = new BoardPoint(4, 0);
        var p2 = new BoardPoint(5, 0);
        var effectAtP1 = MakeEffect(Side.Red, EffectStateValue.Active, p1);
        var effectAtP2 = MakeEffect(Side.Red, EffectStateValue.Active, p2);
        state.EffectInstances.AddRange(new[] { effectAtP1, effectAtP2 });

        var result = state.EffectsAtPosition(p1).ToList();

        Assert.Single(result);
        Assert.Same(effectAtP1, result[0]);
    }

    [Fact]
    public void EffectQueries_ActiveEffectsAtPosition_excludes_ended()
    {
        var pos = new BoardPoint(4, 0);
        var state = EmptyState();
        var active = MakeEffect(Side.Red, EffectStateValue.Active, pos);
        var ended = MakeEffect(Side.Red, EffectStateValue.Ended, pos);
        state.EffectInstances.AddRange(new[] { active, ended });

        var result = state.ActiveEffectsAtPosition(pos).ToList();

        Assert.Single(result);
        Assert.Same(active, result[0]);
    }

    [Fact]
    public void EffectQueries_EffectsByCode_filters_by_code()
    {
        var state = EmptyState();
        var vanCoc = MakeEffect(Side.Red, EffectStateValue.Active, code: "van_coc_tran_giang");
        var phanKy = MakeEffect(Side.Red, EffectStateValue.Active, code: "phan_ky_doat_the");
        state.EffectInstances.AddRange(new[] { vanCoc, phanKy });

        var result = state.EffectsByCode("van_coc_tran_giang").ToList();

        Assert.Single(result);
        Assert.Same(vanCoc, result[0]);
    }

    [Fact]
    public void EffectQueries_ActiveEffectsByCode_combines_filters()
    {
        var state = EmptyState();
        var active = MakeEffect(Side.Red, EffectStateValue.Active, code: "test");
        var ended = MakeEffect(Side.Red, EffectStateValue.Ended, code: "test");
        state.EffectInstances.AddRange(new[] { active, ended });

        var result = state.ActiveEffectsByCode("test").ToList();

        Assert.Single(result);
        Assert.Same(active, result[0]);
    }

    #endregion

    #region StateSchemaUpgrade

    [Fact]
    public void StateSchemaUpgrade_v3_to_v4_adds_EffectInstances()
    {
        // Create a v3 state (simulate pre-Effect system)
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 10,
            SideToMove = Side.Red
        };

        var upgraded = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        Assert.Equal(4, upgraded.StateSchemaVersion);
        Assert.NotNull(upgraded.EffectInstances);
        Assert.Empty(upgraded.EffectInstances);
    }

    [Fact]
    public void StateSchemaUpgrade_v3_to_v4_adds_NextCreationOrder()
    {
        var v3State = new GameState { StateSchemaVersion = 3 };

        var upgraded = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        Assert.Equal(0, upgraded.NextCreationOrder);
    }

    [Fact]
    public void StateSchemaUpgrade_v3_to_v4_adds_ProcessedTurns()
    {
        var v3State = new GameState { StateSchemaVersion = 3 };

        var upgraded = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        Assert.NotNull(upgraded.ProcessedTurns);
        Assert.NotNull(upgraded.ProcessedTurns[Side.Red]);
        Assert.NotNull(upgraded.ProcessedTurns[Side.Black]);
        Assert.Empty(upgraded.ProcessedTurns[Side.Red]);
        Assert.Empty(upgraded.ProcessedTurns[Side.Black]);
    }

    [Fact]
    public void StateSchemaUpgrade_v3_to_v4_preserves_existing_fields()
    {
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 15,
            SideToMove = Side.Black,
            Version = 42
        };

        var upgraded = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        Assert.Equal(15, upgraded.TurnIndex);
        Assert.Equal(Side.Black, upgraded.SideToMove);
        Assert.Equal(42, upgraded.Version);
    }

    [Fact]
    public void StateSchemaUpgrade_v4_returns_same_object()
    {
        var v4State = new GameState
        {
            StateSchemaVersion = 4,
            TurnIndex = 5,
            EffectInstances = new List<EffectInstance>(),
            NextCreationOrder = 3,
            ProcessedTurns = new Dictionary<Side, List<int>>
            {
                [Side.Red] = new List<int> { 1, 2 },
                [Side.Black] = new List<int>()
            }
        };

        var result = StateSchemaUpgrade.UpgradeToCurrent(v4State);

        Assert.Same(v4State, result);
    }

    [Fact]
    public void StateSchemaUpgrade_v5_returns_same_object()
    {
        var v5State = new GameState { StateSchemaVersion = 5 };

        var result = StateSchemaUpgrade.UpgradeToCurrent(v5State);

        Assert.Same(v5State, result);
    }

    [Fact]
    public void StateSchemaUpgrade_IsCurrentSchema_v4_returns_true()
    {
        var state = new GameState { StateSchemaVersion = 4 };

        Assert.True(StateSchemaUpgrade.IsCurrentSchema(state));
    }

    [Fact]
    public void StateSchemaUpgrade_IsCurrentSchema_v3_returns_false()
    {
        var state = new GameState { StateSchemaVersion = 3 };

        Assert.False(StateSchemaUpgrade.IsCurrentSchema(state));
    }

    #endregion

    #region GameState.Clone with v4 fields

    [Fact]
    public void GameState_Clone_copies_EffectInstances()
    {
        var state = EmptyState();
        var effectId = Guid.NewGuid();
        var effect = MakeEffect(Side.Red, EffectStateValue.Active, effectId: effectId);
        state.EffectInstances.Add(effect);
        state.NextCreationOrder = 5;
        state.ProcessedTurns[Side.Red].Add(1);

        var clone = state.Clone();

        Assert.Single(clone.EffectInstances);
        Assert.Equal(effectId, clone.EffectInstances[0].EffectId);
        Assert.Equal(5, clone.NextCreationOrder);
        Assert.Single(clone.ProcessedTurns[Side.Red]);
        Assert.Equal(1, clone.ProcessedTurns[Side.Red][0]);
    }

    [Fact]
    public void GameState_Clone_EffectInstances_are_independent()
    {
        var state = EmptyState();
        var effect = MakeEffect(Side.Red, EffectStateValue.Active);
        state.EffectInstances.Add(effect);

        var clone = state.Clone();

        Assert.NotSame(state.EffectInstances, clone.EffectInstances);
        Assert.NotSame(state.EffectInstances[0], clone.EffectInstances[0]);
    }

    [Fact]
    public void GameState_Clone_ProcessedTurns_are_independent()
    {
        var state = EmptyState();
        state.ProcessedTurns[Side.Red].Add(1);
        state.ProcessedTurns[Side.Black].Add(2);

        var clone = state.Clone();

        Assert.NotSame(state.ProcessedTurns, clone.ProcessedTurns);
        Assert.NotSame(state.ProcessedTurns[Side.Red], clone.ProcessedTurns[Side.Red]);
        Assert.NotSame(state.ProcessedTurns[Side.Black], clone.ProcessedTurns[Side.Black]);
        clone.ProcessedTurns[Side.Red].Add(3);
        Assert.Single(state.ProcessedTurns[Side.Red]);
    }

    #endregion

    #region Phase 3.4 — v3 → v4 State Schema Upgrade Integration Tests

    // These tests verify the v3 → v4 state schema upgrade integration.
    // They cover the StateSchemaUpgrade.UpgradeToCurrent() helper and its usage in API services.

    [Fact]
    public void StateSchemaUpgrade_v3_to_v4_upgrades_version()
    {
        // Arrange: Create a v3 state (no EffectInstances, ProcessedTurns, etc.)
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 10,
            SideToMove = Side.Red,
            Version = 5
        };

        // Act
        var upgraded = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        // Assert: Version is upgraded
        Assert.Equal(4, upgraded.StateSchemaVersion);
    }

    [Fact]
    public void StateSchemaUpgrade_v3_to_v4_initializes_all_v4_fields()
    {
        // Arrange
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 10,
            SideToMove = Side.Black
        };

        // Act
        var upgraded = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        // Assert: All v4 collections are initialized
        Assert.NotNull(upgraded.EffectInstances);
        Assert.Empty(upgraded.EffectInstances);

        Assert.NotNull(upgraded.ProcessedTurns);
        Assert.NotNull(upgraded.ProcessedTurns[Side.Red]);
        Assert.NotNull(upgraded.ProcessedTurns[Side.Black]);
        Assert.Empty(upgraded.ProcessedTurns[Side.Red]);
        Assert.Empty(upgraded.ProcessedTurns[Side.Black]);

        Assert.NotNull(upgraded.StakeMetadata);
        Assert.Empty(upgraded.StakeMetadata);

        Assert.Equal(0, upgraded.NextCreationOrder);
    }

    [Fact]
    public void StateSchemaUpgrade_v4_returns_same_object_idempotent()
    {
        // Arrange: Create a v4 state
        var v4State = new GameState
        {
            StateSchemaVersion = 4,
            TurnIndex = 10,
            EffectInstances = new List<EffectInstance>(),
            ProcessedTurns = new Dictionary<Side, List<int>>
            {
                [Side.Red] = new List<int> { 1, 2 },
                [Side.Black] = new List<int>()
            },
            StakeMetadata = new Dictionary<Guid, StakeMetadata>()
        };

        // Act: Call UpgradeToCurrent twice
        var first = StateSchemaUpgrade.UpgradeToCurrent(v4State);
        var second = StateSchemaUpgrade.UpgradeToCurrent(first);

        // Assert: Same object returned (no mutation)
        Assert.Same(v4State, first);
        Assert.Same(v4State, second);
        Assert.Equal(4, first.StateSchemaVersion);
    }

    [Fact]
    public void StateSchemaUpgrade_idempotent_twice_on_v3()
    {
        // Arrange: Create a v3 state
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 10,
            SideToMove = Side.Red
        };

        // Act: Upgrade twice
        var first = StateSchemaUpgrade.UpgradeToCurrent(v3State);
        var second = StateSchemaUpgrade.UpgradeToCurrent(first);

        // Assert: Both are valid v4 states with same version
        Assert.Equal(4, first.StateSchemaVersion);
        Assert.Equal(4, second.StateSchemaVersion);

        // Running upgrade twice produces the same state
        Assert.Equal(first.TurnIndex, second.TurnIndex);
        Assert.Equal(first.SideToMove, second.SideToMove);
        Assert.Equal(first.NextCreationOrder, second.NextCreationOrder);
        Assert.Equal(first.EffectInstances.Count, second.EffectInstances.Count);
        Assert.Equal(first.ProcessedTurns.Count, second.ProcessedTurns.Count);
        Assert.Equal(first.StakeMetadata.Count, second.StakeMetadata.Count);
    }

    [Fact]
    public void StateSchemaUpgrade_preserves_existing_gameplay_fields()
    {
        // Arrange: v3 state with gameplay data
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 15,
            SideToMove = Side.Black,
            Version = 42,
            Result = null,
            EndReason = null,
            CountedActions = 100,
            Pieces = new List<PieceState>
            {
                new PieceState { PieceId = Guid.NewGuid(), HeroId = Guid.NewGuid(), Side = Side.Red, Class = PieceClass.General, SetupPoints = 0, Position = new BoardPoint(4, 0), StartPosition = new BoardPoint(4, 0) },
                new PieceState { PieceId = Guid.NewGuid(), HeroId = Guid.NewGuid(), Side = Side.Black, Class = PieceClass.General, SetupPoints = 0, Position = new BoardPoint(4, 9), StartPosition = new BoardPoint(4, 9) }
            }
        };

        // Act
        var upgraded = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        // Assert: All gameplay fields preserved
        Assert.Equal(15, upgraded.TurnIndex);
        Assert.Equal(Side.Black, upgraded.SideToMove);
        Assert.Equal(42, upgraded.Version);
        Assert.Equal(100, upgraded.CountedActions);
        Assert.Equal(2, upgraded.Pieces.Count);
        Assert.Null(upgraded.Result);
        Assert.Null(upgraded.EndReason);
    }

    [Fact]
    public void StateSchemaUpgrade_handles_future_versions()
    {
        // Arrange: Future version (should be treated as current)
        var futureState = new GameState
        {
            StateSchemaVersion = 99,
            TurnIndex = 5
        };

        // Act
        var result = StateSchemaUpgrade.UpgradeToCurrent(futureState);

        // Assert: Returned as-is (no upgrade attempted)
        Assert.Same(futureState, result);
        Assert.Equal(99, result.StateSchemaVersion);
    }

    [Fact]
    public void StateSchemaUpgrade_creates_separate_object_for_v3()
    {
        // Arrange
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 10,
            SideToMove = Side.Red
        };

        // Act
        var upgraded = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        // Assert: Different object
        Assert.NotSame(v3State, upgraded);

        // But gameplay fields are the same
        Assert.Equal(v3State.TurnIndex, upgraded.TurnIndex);
        Assert.Equal(v3State.SideToMove, upgraded.SideToMove);
    }

    #endregion

    #region Phase 3.4 — TurnLifecycle with v3 state (NRE prevention)

    // These tests verify that TurnLifecycle can process a v3 state after upgrade
    // without NullReferenceException.

    [Fact]
    public void TurnLifecycle_Apply_works_after_v3_upgrade()
    {
        // Arrange: v3 state upgraded to v4
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 1,
            SideToMove = Side.Red,
            SkillStates = new Dictionary<Side, List<SkillState>>
            {
                [Side.Red] = new List<SkillState> { new SkillState(1, Guid.NewGuid(), null, 3, "test") },
                [Side.Black] = new List<SkillState>()
            }
        };
        var state = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        // Act: TurnLifecycle should work without NRE
        var result = TurnLifecycle.Apply(state, Side.Red);

        // Assert: Lifecycle ran successfully
        Assert.NotNull(result);
        Assert.Equal(2, result.State.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Single(result.DecrementedCooldowns);
    }

    [Fact]
    public void TurnLifecycle_IsTurnProcessed_works_after_v3_upgrade()
    {
        // Arrange: v3 state upgraded to v4
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 1,
            SideToMove = Side.Red
        };
        var state = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        // Act & Assert: Should not throw NRE
        var isProcessed = TurnLifecycle.IsTurnProcessed(state, Side.Red, 1);
        Assert.False(isProcessed);
    }

    [Fact]
    public void TurnLifecycle_MarkTurnProcessed_works_after_v3_upgrade()
    {
        // Arrange: v3 state upgraded to v4
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 1,
            SideToMove = Side.Red
        };
        var state = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        // Act: Should not throw NRE
        TurnLifecycle.MarkTurnProcessed(state, Side.Red, 1);

        // Assert: Turn is marked
        Assert.True(TurnLifecycle.IsTurnProcessed(state, Side.Red, 1));
    }

    [Fact]
    public void TurnLifecycle_Apply_decrements_cooldown_on_upgraded_v3_state()
    {
        // Arrange: v3 state with cooldown
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 1,
            SideToMove = Side.Black,
            SkillStates = new Dictionary<Side, List<SkillState>>
            {
                [Side.Red] = new List<SkillState>(),
                [Side.Black] = new List<SkillState> { new SkillState(1, Guid.NewGuid(), null, 5, "test") }
            }
        };
        var state = StateSchemaUpgrade.UpgradeToCurrent(v3State);

        // Act
        var result = TurnLifecycle.Apply(state, Side.Black);

        // Assert: Cooldown decremented
        Assert.Equal(4, result.State.SkillStates[Side.Black][0].CooldownRemaining);
        Assert.Single(result.DecrementedCooldowns);
    }

    [Fact]
    public void TurnLifecycle_Apply_effect_expires_on_upgraded_v3_state()
    {
        // Arrange: v3 state with effect at duration 1
        var v3State = new GameState
        {
            StateSchemaVersion = 3,
            TurnIndex = 1,
            SideToMove = Side.Red,
            EffectInstances = new List<EffectInstance>()
        };
        // Manually add effect after upgrade (simulating pre-existing effect)
        var state = StateSchemaUpgrade.UpgradeToCurrent(v3State);
        state.EffectInstances.Add(new EffectInstance
        {
            EffectId = Guid.NewGuid(),
            Code = "test",
            SkillId = Guid.NewGuid(),
            Creator = Side.Red,
            CreationOrder = 1,
            State = EffectStateValue.Active,
            Duration = 1,
            RemainingDuration = 1,
            TargetPositions = new List<BoardPoint> { new(4, 0) },
            PositionControllers = new Dictionary<BoardPoint, Side> { [new BoardPoint(4, 0)] = Side.Red }
        });

        // Act
        var result = TurnLifecycle.Apply(state, Side.Red);

        // Assert: Effect expired
        Assert.Equal(EffectStateValue.Ended, result.State.EffectInstances[0].State);
        Assert.Single(result.ExpiredEffects);
    }

    #endregion

    #region Helper methods

    private static GameState EmptyState() => new()
    {
        StateSchemaVersion = 4,
        EffectInstances = new List<EffectInstance>(),
        ProcessedTurns = new Dictionary<Side, List<int>>
        {
            [Side.Red] = new List<int>(),
            [Side.Black] = new List<int>()
        }
    };

    private static EffectInstance MakeEffect(
        Side creator,
        EffectStateValue state,
        BoardPoint? position = null,
        Guid? effectId = null,
        string? code = null)
    {
        var pos = position ?? new BoardPoint(4, 0);
        return new EffectInstance
        {
            EffectId = effectId ?? Guid.NewGuid(),
            Code = code ?? "test_effect",
            SkillId = Guid.NewGuid(),
            Creator = creator,
            CreationOrder = 1,
            State = state,
            Duration = 2,
            RemainingDuration = 2,
            TargetPositions = new List<BoardPoint> { pos },
            PositionControllers = new Dictionary<BoardPoint, Side> { [pos] = creator }
        };
    }

    #endregion
}

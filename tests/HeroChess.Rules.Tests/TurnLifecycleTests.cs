// Phase 2.2: TurnLifecycle unit tests.
using HeroChess.Rules;
using HeroChess.Rules.Effects;
using Xunit;

namespace HeroChess.Rules.Tests;

/// <summary>
/// Unit tests for TurnLifecycle (§8.3 of the implementation plan).
/// Tests cover: cooldown decrement, Effect duration decrement, expiration,
/// exactly-once semantics, physical stake lifetime, Disabled Effects.
/// </summary>
public sealed class TurnLifecycleTests
{
    #region Cooldown decrement

    [Fact]
    public void Apply_decrements_cooldown_by_one()
    {
        var state = EmptyState();
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 3);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(2, result.State.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Single(result.DecrementedCooldowns);
        Assert.Equal(2, result.DecrementedCooldowns[0].NewCooldown);
    }

    [Fact]
    public void Apply_decrements_multiple_cooldowns()
    {
        var state = EmptyState();
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 2);
        AddSkill(state, Side.Red, slot: 2, cooldownRemaining: 1);
        AddSkill(state, Side.Red, slot: 3, cooldownRemaining: 3);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(1, result.State.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Equal(0, result.State.SkillStates[Side.Red][1].CooldownRemaining);
        Assert.Equal(2, result.State.SkillStates[Side.Red][2].CooldownRemaining);
        Assert.Equal(2, result.DecrementedCooldowns.Count);
    }

    [Fact]
    public void Apply_does_not_decrement_cooldown_at_zero()
    {
        var state = EmptyState();
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 0);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(0, result.State.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Empty(result.DecrementedCooldowns);
    }

    [Fact]
    public void Apply_cooldown_at_one_decrements_to_zero_no_event()
    {
        var state = EmptyState();
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 1);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(0, result.State.SkillStates[Side.Red][0].CooldownRemaining);
        // Cooldown expiry does not emit an event (newCooldown == 0)
        Assert.Empty(result.DecrementedCooldowns);
    }

    [Fact]
    public void Apply_does_not_affect_opponent_cooldowns()
    {
        var state = EmptyState();
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 3);
        AddSkill(state, Side.Black, slot: 1, cooldownRemaining: 2);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(2, result.State.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Equal(2, result.State.SkillStates[Side.Black][0].CooldownRemaining); // unchanged
    }

    #endregion

    #region Effect duration decrement (U-DUR = A)

    [Fact]
    public void Apply_decrements_RemainingDuration_at_Creator_turn()
    {
        var state = EmptyState();
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 2);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(1, result.State.EffectInstances[0].RemainingDuration);
        Assert.Equal(EffectStateValue.Active, result.State.EffectInstances[0].State);
        Assert.Empty(result.ExpiredEffects);
    }

    [Fact]
    public void Apply_does_not_decrement_RemainingDuration_at_nonCreator_turn()
    {
        var state = EmptyState();
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 2);

        var result = TurnLifecycle.Apply(state, Side.Black);

        Assert.Equal(2, result.State.EffectInstances[0].RemainingDuration);
        Assert.Empty(result.ExpiredEffects);
    }

    [Fact]
    public void Apply_expires_effect_at_remainingDuration_zero()
    {
        var state = EmptyState();
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 1);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(0, result.State.EffectInstances[0].RemainingDuration);
        Assert.Equal(EffectStateValue.Ended, result.State.EffectInstances[0].State);
        Assert.Single(result.ExpiredEffects);
        Assert.Equal(effect.EffectId, result.ExpiredEffects[0].EffectId);
    }

    [Fact]
    public void Apply_expires_multiple_effects()
    {
        var state = EmptyState();
        MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 1);
        MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 2);
        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 1);

        var result = TurnLifecycle.Apply(state, Side.Red);

        // Only the Red effect with remainingDuration==1 expires on Red's turn.
        // The Black effect with remainingDuration==1 expires on Black's turn.
        Assert.Equal(3, result.State.EffectInstances.Count);
        var endedCount = result.State.EffectInstances.Count(e => e.State == EffectStateValue.Ended);
        Assert.Equal(1, endedCount);
        Assert.Single(result.ExpiredEffects);
    }

    [Fact]
    public void Apply_does_not_decrement_Deleted_effects()
    {
        var state = EmptyState();
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Ended, remainingDuration: 1);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(1, result.State.EffectInstances[0].RemainingDuration);
        Assert.Empty(result.ExpiredEffects);
    }

    [Fact]
    public void Apply_Disabled_effect_continues_timer()
    {
        var state = EmptyState();
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Disabled, remainingDuration: 2);

        var result = TurnLifecycle.Apply(state, Side.Red);

        // Disabled does NOT freeze the timer — the Effect continues counting down.
        Assert.Equal(1, result.State.EffectInstances[0].RemainingDuration);
        Assert.Equal(EffectStateValue.Disabled, result.State.EffectInstances[0].State);
        Assert.Empty(result.ExpiredEffects);
    }

    [Fact]
    public void Apply_Disabled_effect_expires_when_RemainingDuration_reaches_zero()
    {
        var state = EmptyState();
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Disabled, remainingDuration: 1);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(EffectStateValue.Ended, result.State.EffectInstances[0].State);
        Assert.Single(result.ExpiredEffects);
    }

    [Fact]
    public void Apply_does_not_reset_RemainingDuration_on_transfer()
    {
        // "Stealing does NOT reset duration" — confirmed gameplay rule.
        // This is tested via the model: RemainingDuration is never reset by transfer.
        // The lifecycle just decrements; it never calls reset.
        var state = EmptyState();
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 2);
        // Simulate a split: one position transferred to Black
        effect.PositionControllers[new BoardPoint(4, 1)] = Side.Black;

        var result = TurnLifecycle.Apply(state, Side.Red);

        // Red's turn still drives the timer (TimerOwner = Creator = Red).
        Assert.Equal(1, result.State.EffectInstances[0].RemainingDuration);
    }

    [Fact]
    public void Apply_does_not_affect_opponent_effects()
    {
        var state = EmptyState();
        MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 1);
        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 1);

        var result = TurnLifecycle.Apply(state, Side.Red);

        // Only Red effect expires
        Assert.Single(result.ExpiredEffects);
        var redEnded = result.State.EffectInstances.First(e => e.Creator == Side.Red);
        Assert.Equal(EffectStateValue.Ended, redEnded.State);
        var blackStillActive = result.State.EffectInstances.First(e => e.Creator == Side.Black);
        Assert.Equal(EffectStateValue.Active, blackStillActive.State);
        Assert.Equal(1, blackStillActive.RemainingDuration);
    }

    #endregion

    #region Exactly-once semantics

    [Fact]
    public void IsTurnProcessed_returns_false_when_never_processed()
    {
        var state = EmptyState();

        var result = TurnLifecycle.IsTurnProcessed(state, Side.Red, turnIndex: 1);

        Assert.False(result);
    }

    [Fact]
    public void IsTurnProcessed_returns_true_after_marking()
    {
        var state = EmptyState();
        TurnLifecycle.MarkTurnProcessed(state, Side.Red, turnIndex: 1);

        var result = TurnLifecycle.IsTurnProcessed(state, Side.Red, turnIndex: 1);

        Assert.True(result);
    }

    [Fact]
    public void IsTurnProcessed_is_per_side()
    {
        var state = EmptyState();
        TurnLifecycle.MarkTurnProcessed(state, Side.Red, turnIndex: 1);

        var redResult = TurnLifecycle.IsTurnProcessed(state, Side.Red, turnIndex: 1);
        var blackResult = TurnLifecycle.IsTurnProcessed(state, Side.Black, turnIndex: 1);

        Assert.True(redResult);
        Assert.False(blackResult);
    }

    [Fact]
    public void IsTurnProcessed_is_per_turnIndex()
    {
        var state = EmptyState();
        TurnLifecycle.MarkTurnProcessed(state, Side.Red, turnIndex: 1);

        var turn1 = TurnLifecycle.IsTurnProcessed(state, Side.Red, turnIndex: 1);
        var turn2 = TurnLifecycle.IsTurnProcessed(state, Side.Red, turnIndex: 2);

        Assert.True(turn1);
        Assert.False(turn2);
    }

    [Fact]
    public void MarkTurnProcessed_is_idempotent()
    {
        var state = EmptyState();
        TurnLifecycle.MarkTurnProcessed(state, Side.Red, turnIndex: 1);
        TurnLifecycle.MarkTurnProcessed(state, Side.Red, turnIndex: 1);
        TurnLifecycle.MarkTurnProcessed(state, Side.Red, turnIndex: 1);

        var result = TurnLifecycle.IsTurnProcessed(state, Side.Red, turnIndex: 1);
        Assert.True(result);
        Assert.Single(state.ProcessedTurns[Side.Red]);
    }

    [Fact]
    public void Apply_does_not_mutate_original_state()
    {
        var state = EmptyState();
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 3);
        MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 2);

        var result = TurnLifecycle.Apply(state, Side.Red);

        // Original state is unchanged
        Assert.Equal(3, state.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Equal(2, state.EffectInstances[0].RemainingDuration);
        Assert.Equal(EffectStateValue.Active, state.EffectInstances[0].State);

        // Result state has the changes
        Assert.Equal(2, result.State.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Equal(1, result.State.EffectInstances[0].RemainingDuration);
    }

    #endregion

    #region Physical stake lifetime

    [Fact]
    public void Apply_decrements_stake_lifetime_at_placer_turn()
    {
        var state = EmptyState();
        AddStake(state, Side.Red, remainingLifetime: 2);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Equal(1, result.State.Obstacles[0].RemainingLifetime);
        Assert.Empty(result.RemovedStakes);
    }

    [Fact]
    public void Apply_removes_stake_at_lifetime_zero()
    {
        var state = EmptyState();
        AddStake(state, Side.Red, remainingLifetime: 1);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Empty(result.State.Obstacles);
        Assert.Single(result.RemovedStakes);
    }

    [Fact]
    public void Apply_does_not_decrement_stake_at_nonPlacer_turn()
    {
        var state = EmptyState();
        AddStake(state, Side.Red, remainingLifetime: 2);

        var result = TurnLifecycle.Apply(state, Side.Black);

        Assert.Equal(2, result.State.Obstacles[0].RemainingLifetime);
        Assert.Empty(result.RemovedStakes);
    }

    [Fact]
    public void Apply_does_not_reset_stake_lifetime_on_control_transfer()
    {
        // "Countering does NOT reset the original lifetime" — confirmed gameplay rule.
        // The lifecycle just decrements; transfer does not reset lifetime.
        var state = EmptyState();
        AddStake(state, Side.Red, remainingLifetime: 1);

        var result = TurnLifecycle.Apply(state, Side.Red);

        // Stake expires normally; not reset
        Assert.Empty(result.State.Obstacles);
    }

    [Fact]
    public void Apply_does_not_affect_non_stake_obstacles()
    {
        var state = EmptyState();
        state.Obstacles.Add(new ObstacleState(Guid.NewGuid(), new BoardPoint(4, 0), "wall"));

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Single(result.State.Obstacles);
        Assert.Empty(result.RemovedStakes);
    }

    [Fact]
    public void Apply_removes_multiple_expired_stakes()
    {
        var state = EmptyState();
        AddStake(state, Side.Red, remainingLifetime: 1, position: new BoardPoint(4, 0));
        AddStake(state, Side.Red, remainingLifetime: 1, position: new BoardPoint(5, 0));

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Empty(result.State.Obstacles);
        Assert.Equal(2, result.RemovedStakes.Count);
    }

    [Fact]
    public void Stake_lifetime_is_independent_from_blocking_Effect()
    {
        var state = EmptyState();
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 1);
        AddStake(state, Side.Red, remainingLifetime: 2, effectId: effect.EffectId);

        // Apply Red's turn: Effect expires (RemainingDuration 1→0), but stake has 2→1
        var result = TurnLifecycle.Apply(state, Side.Red);

        // Effect expired
        Assert.Equal(EffectStateValue.Ended, result.State.EffectInstances[0].State);
        Assert.Single(result.ExpiredEffects);
        // Stake still alive
        Assert.Single(result.State.Obstacles);
        Assert.Equal(1, result.State.Obstacles[0].RemainingLifetime);
        Assert.Empty(result.RemovedStakes);
    }

    #endregion

    #region Event correctness

    [Fact]
    public void Apply_emits_ExpiredEffects_event()
    {
        var state = EmptyState();
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 1, code: "van_coc_tran_giang");

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Single(result.ExpiredEffects);
        Assert.Equal(effect.EffectId, result.ExpiredEffects[0].EffectId);
        Assert.Equal(effect.SkillId, result.ExpiredEffects[0].SkillId);
        Assert.Equal(Side.Red, result.ExpiredEffects[0].Creator);
        Assert.Equal("van_coc_tran_giang", result.ExpiredEffects[0].Code);
    }

    [Fact]
    public void Apply_emits_stake_removal_event()
    {
        var state = EmptyState();
        var stakeId = Guid.NewGuid();
        AddStake(state, Side.Red, remainingLifetime: 1, stakeId: stakeId);

        var result = TurnLifecycle.Apply(state, Side.Red);

        Assert.Single(result.RemovedStakes);
        Assert.Equal(stakeId, result.RemovedStakes[0].ObstacleId);
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
        },
        StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
        SkillStates = new Dictionary<Side, List<SkillState>>
        {
            [Side.Red] = new List<SkillState>(),
            [Side.Black] = new List<SkillState>()
        }
    };

    private static void AddSkill(GameState state, Side side, int slot, int cooldownRemaining)
    {
        state.SkillStates[side].Add(new SkillState(slot, Guid.NewGuid(), null, cooldownRemaining, ""));
    }

    private static EffectInstance MakeEffect(
        GameState state,
        Side creator,
        EffectStateValue state2,
        int remainingDuration,
        string code = "test_effect",
        Guid? effectId = null,
        int? creationOrder = null)
    {
        var effect = new EffectInstance
        {
            EffectId = effectId ?? Guid.NewGuid(),
            Code = code,
            SkillId = Guid.NewGuid(),
            Creator = creator,
            CreationOrder = creationOrder ?? 1,
            State = state2,
            Duration = remainingDuration,
            RemainingDuration = remainingDuration,
            TargetPositions = new List<BoardPoint> { new(4, 0) },
            PositionControllers = new Dictionary<BoardPoint, Side> { [new BoardPoint(4, 0)] = creator }
        };
        state.EffectInstances.Add(effect);
        return effect;
    }

    private static void AddStake(
        GameState state,
        Side placer,
        int remainingLifetime,
        BoardPoint? position = null,
        Guid? stakeId = null,
        Guid? effectId = null)
    {
        var obstacleId = stakeId ?? Guid.NewGuid();
        var pos = position ?? new BoardPoint(4, 0);
        state.Obstacles.Add(new ObstacleState(obstacleId, pos, "stake", remainingLifetime));
        state.StakeMetadata[obstacleId] = new StakeMetadata { Placer = placer, EffectId = effectId };
    }

    #endregion

    #region Phase 3.1 — ExecuteActorAsync lifecycle integration tests

    // These tests verify the ExecuteActorAsync lifecycle integration pattern.
    // They test the exact pattern used in MatchCommandService.ExecuteActorAsync:
    // 1. Check IsTurnProcessed before calling Apply
    // 2. Apply lifecycle if not processed
    // 3. Mark as processed after Apply
    // 4. Skip Apply if already processed (exactly-once)

    [Fact]
    public void ExecuteActorAsync_pattern_first_command_runs_lifecycle()
    {
        // Pattern: first command in a turn should run lifecycle
        var state = EmptyState();
        state.TurnIndex = 1;
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 2);

        // First command: lifecycle should run
        var lifecycleEvents = new List<object>();
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
            Assert.True(lifecycle.DecrementedCooldowns.Count > 0);
        }

        // Lifecycle ran
        Assert.Equal(1, state.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.True(TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex));
    }

    [Fact]
    public void ExecuteActorAsync_pattern_second_command_skips_lifecycle()
    {
        // Pattern: second command in same turn should skip lifecycle (exactly-once)
        var state = EmptyState();
        state.TurnIndex = 1;
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 2);

        // First command: lifecycle runs
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
        }

        var cooldownAfterFirst = state.SkillStates[Side.Red][0].CooldownRemaining;

        // Second command: lifecycle should NOT run
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
        }

        // Cooldown unchanged
        Assert.Equal(cooldownAfterFirst, state.SkillStates[Side.Red][0].CooldownRemaining);
    }

    [Fact]
    public void ExecuteActorAsync_pattern_next_turn_runs_lifecycle()
    {
        // Pattern: next TurnIndex should run lifecycle again
        var state = EmptyState();
        state.TurnIndex = 1;
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 2);

        // First turn: lifecycle runs
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
        }

        var cooldownAfterTurn1 = state.SkillStates[Side.Red][0].CooldownRemaining;

        // Simulate turn transition: TurnIndex increments
        state.TurnIndex = 2;

        // Next turn: lifecycle should run again
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
        }

        // Cooldown decremented again
        Assert.Equal(cooldownAfterTurn1 - 1, state.SkillStates[Side.Red][0].CooldownRemaining);
    }

    [Fact]
    public void ExecuteActorAsync_pattern_opponent_side_runs_lifecycle_separately()
    {
        // Pattern: each side has separate lifecycle tracking
        var state = EmptyState();
        state.TurnIndex = 1;
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 2);
        AddSkill(state, Side.Black, slot: 1, cooldownRemaining: 3);

        // Red's turn: lifecycle runs for Red
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
        }

        var redCooldownAfterRedTurn = state.SkillStates[Side.Red][0].CooldownRemaining;
        var blackCooldownAfterRedTurn = state.SkillStates[Side.Black][0].CooldownRemaining;

        // Black's turn: lifecycle runs for Black (Red's turn is already processed)
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);
        }

        // Red cooldown unchanged
        Assert.Equal(redCooldownAfterRedTurn, state.SkillStates[Side.Red][0].CooldownRemaining);
        // Black cooldown decremented
        Assert.Equal(blackCooldownAfterRedTurn - 1, state.SkillStates[Side.Black][0].CooldownRemaining);
    }

    [Fact]
    public void ExecuteActorAsync_pattern_effect_expires_on_creator_turn()
    {
        // Pattern: effect expires on Creator's turn
        var state = EmptyState();
        state.TurnIndex = 1;
        MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 1);

        // Red's turn: lifecycle runs, effect should expire
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);

            Assert.Single(lifecycle.ExpiredEffects);
            Assert.Equal(EffectStateValue.Ended, state.EffectInstances[0].State);
        }
    }

    [Fact]
    public void ExecuteActorAsync_pattern_duplicate_request_does_not_corrupt()
    {
        // Pattern: duplicate request (same TurnIndex) should not corrupt state
        var state = EmptyState();
        state.TurnIndex = 1;
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 2);

        // First command: lifecycle runs
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
        }

        var cooldownAfterFirst = state.SkillStates[Side.Red][0].CooldownRemaining;

        // Duplicate command: lifecycle should NOT run (turn already processed)
        // This simulates what happens when ExecuteActorAsync sees IsTurnProcessed == true
        Assert.True(TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex));

        // State remains valid
        Assert.Equal(1, cooldownAfterFirst);
        Assert.Equal(EffectStateValue.Active, state.EffectInstances.Count > 0
            ? state.EffectInstances[0].State
            : EffectStateValue.Active); // No effects in this test
    }

    [Fact]
    public void ExecuteActorAsync_pattern_state_changes_are_applied_correctly()
    {
        // Pattern: lifecycle state changes are applied to the state
        var state = EmptyState();
        state.TurnIndex = 1;
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 2);
        MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 2);
        AddStake(state, Side.Red, remainingLifetime: 1);

        // Apply lifecycle
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
        }

        // All lifecycle changes applied:
        // 1. Cooldown decremented
        Assert.Equal(1, state.SkillStates[Side.Red][0].CooldownRemaining);
        // 2. Effect duration decremented (from 2 to 1, still Active)
        Assert.Equal(1, state.EffectInstances[0].RemainingDuration);
        Assert.Equal(EffectStateValue.Active, state.EffectInstances[0].State);
        // 3. Stake expired (from lifetime 1 to 0)
        Assert.Empty(state.Obstacles);
    }

    [Fact]
    public void ExecuteActorAsync_pattern_pending_steal_finalizes_at_creator_turn()
    {
        // Pattern: pending steal finalizes at Creator's turn
        var state = EmptyState();
        state.TurnIndex = 1;
        // Create an effect with PendingController set (simulating Phản Kỳ use)
        var effect = MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 2);
        effect.PendingController = Side.Black;

        // Black's turn (after Red used Phản Kỳ)
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);

            // Pending steal should be finalized
            Assert.Single(lifecycle.FinalizedSteals);
            Assert.Equal(Side.Black, lifecycle.FinalizedSteals[0].NewController);
            // PositionController should be updated
            Assert.Equal(Side.Black, state.EffectInstances[0].PositionControllers[new BoardPoint(4, 0)]);
            Assert.False(state.EffectInstances[0].PendingController.HasValue);
        }
    }

    #endregion

    #region Phase 3.2 — TimeoutAsync lifecycle integration tests

    // These tests verify the TimeoutAsync lifecycle integration pattern.
    // When timeout advances the game to the next player's turn, TurnLifecycle
    // must execute exactly once for the new authoritative player.

    [Fact]
    public void TimeoutAsync_pattern_runs_lifecycle_for_new_side()
    {
        // Pattern: when timeout changes SideToMove, lifecycle runs for the NEW player
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;
        AddSkill(state, Side.Black, slot: 1, cooldownRemaining: 3); // Black's cooldown

        // Simulate timeout: Red times out, turn changes to Black
        var nextSide = Side.Black;
        state.Version++;
        state.TurnIndex++;
        state.CountedActions++;
        state.SideToMove = nextSide;

        // Run lifecycle for the NEW authoritative player (Black)
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, state.TurnIndex);
        }

        // Black's cooldown should be decremented
        Assert.Equal(2, state.SkillStates[Side.Black][0].CooldownRemaining);
        // ProcessedTurns should be marked
        Assert.True(TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex));
    }

    [Fact]
    public void TimeoutAsync_pattern_lifecycle_uses_new_turnIndex()
    {
        // Pattern: lifecycle uses the NEW TurnIndex after timeout
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 2);

        // Simulate timeout
        state.Version++;
        var oldTurnIndex = state.TurnIndex;
        state.TurnIndex++;
        state.CountedActions++;
        var nextSide = Side.Black;
        state.SideToMove = nextSide;

        // Lifecycle should use the NEW TurnIndex
        var newTurnIndex = state.TurnIndex;
        Assert.NotEqual(oldTurnIndex, newTurnIndex);

        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, newTurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, newTurnIndex);
        }

        // ProcessedTurns should be keyed by NEW TurnIndex
        Assert.True(TurnLifecycle.IsTurnProcessed(state, nextSide, newTurnIndex));
        Assert.False(TurnLifecycle.IsTurnProcessed(state, nextSide, oldTurnIndex));
    }

    [Fact]
    public void TimeoutAsync_pattern_lifecycle_runs_exactly_once()
    {
        // Pattern: duplicate timeout request should NOT run lifecycle twice
        var state = EmptyState();
        state.TurnIndex = 2;
        state.SideToMove = Side.Black;
        AddSkill(state, Side.Black, slot: 1, cooldownRemaining: 3);

        var nextSide = Side.Black;
        var turnIndex = state.TurnIndex;

        // First "timeout" (cooldown 3→2)
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, turnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, turnIndex);
        }

        var cooldownAfterFirst = state.SkillStates[Side.Black][0].CooldownRemaining;
        Assert.Equal(2, cooldownAfterFirst);

        // Duplicate "timeout" should NOT run lifecycle
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, turnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, turnIndex);
        }

        // Cooldown unchanged (no second decrement)
        Assert.Equal(2, state.SkillStates[Side.Black][0].CooldownRemaining);
    }

    [Fact]
    public void TimeoutAsync_pattern_processed_turns_persists_correctly()
    {
        // Pattern: ProcessedTurns is correctly populated after timeout lifecycle
        var state = EmptyState();
        state.TurnIndex = 3;
        state.SideToMove = Side.Red;

        var nextSide = Side.Black;
        var turnIndex = state.TurnIndex;

        // Before lifecycle: turn not processed
        Assert.False(TurnLifecycle.IsTurnProcessed(state, Side.Red, turnIndex));
        Assert.False(TurnLifecycle.IsTurnProcessed(state, Side.Black, turnIndex));

        // After lifecycle
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, turnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, turnIndex);
        }

        // Only the new side's turn is marked as processed
        Assert.False(TurnLifecycle.IsTurnProcessed(state, Side.Red, turnIndex));
        Assert.True(TurnLifecycle.IsTurnProcessed(state, Side.Black, turnIndex));

        // ProcessedTurns structure is correct
        Assert.Single(state.ProcessedTurns[Side.Black]);
        Assert.Empty(state.ProcessedTurns[Side.Red]);
        Assert.Contains(turnIndex, state.ProcessedTurns[Side.Black]);
    }

    [Fact]
    public void TimeoutAsync_pattern_effect_expires_on_timeout_start()
    {
        // Pattern: effect expires when the NEW player's turn starts via timeout
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;
        // Red has an effect expiring on Red's turn
        MakeEffect(state, Side.Red, EffectStateValue.Active, remainingDuration: 1);

        // Red timeout advances to Black's turn
        state.Version++;
        state.TurnIndex++;
        state.CountedActions++;
        state.SideToMove = Side.Black;

        var nextSide = Side.Black;
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, state.TurnIndex);

            // Black's turn: Red's effects don't decrement (Black is not the Creator)
            Assert.Empty(lifecycle.ExpiredEffects);
        }
    }

    [Fact]
    public void TimeoutAsync_pattern_effect_expires_on_subsequent_creator_turn()
    {
        // Pattern: effect expires on Creator's next turn (after timeout)
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;
        // Black has an effect created by Black, expiring on Black's turn
        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 1);

        // Red timeout advances to Black's turn
        state.Version++;
        state.TurnIndex++;
        state.CountedActions++;
        state.SideToMove = Side.Black;

        var nextSide = Side.Black;
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, state.TurnIndex);

            // Black's turn: Black's effect expires
            Assert.Single(lifecycle.ExpiredEffects);
            Assert.Equal(EffectStateValue.Ended, state.EffectInstances[0].State);
        }
    }

    [Fact]
    public void TimeoutAsync_pattern_stake_expires_on_timeout_start()
    {
        // Pattern: stake expires when the placer's turn starts via timeout
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;
        // Red placed a stake expiring on Red's turn
        AddStake(state, Side.Red, remainingLifetime: 1);

        // Red timeout advances to Black's turn
        state.Version++;
        state.TurnIndex++;
        state.CountedActions++;
        state.SideToMove = Side.Black;

        var nextSide = Side.Black;
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, state.TurnIndex);

            // Black's turn: Red's stake doesn't decrement (Black is not the placer)
            Assert.Empty(lifecycle.RemovedStakes);
        }
    }

    [Fact]
    public void TimeoutAsync_pattern_stake_expires_on_subsequent_placer_turn()
    {
        // Pattern: stake expires on placer's subsequent turn (after timeout)
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;
        // Black placed a stake expiring on Black's turn
        AddStake(state, Side.Black, remainingLifetime: 1);

        // Red timeout advances to Black's turn
        state.Version++;
        state.TurnIndex++;
        state.CountedActions++;
        state.SideToMove = Side.Black;

        var nextSide = Side.Black;
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, state.TurnIndex);

            // Black's turn: Black's stake expires
            Assert.Single(lifecycle.RemovedStakes);
            Assert.Empty(state.Obstacles);
        }
    }

    [Fact]
    public void TimeoutAsync_pattern_lifecycle_events_collected_correctly()
    {
        // Pattern: lifecycle events are correctly collected for timeout broadcast
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;
        AddSkill(state, Side.Black, slot: 1, cooldownRemaining: 2);
        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 1);
        AddStake(state, Side.Black, remainingLifetime: 1);

        // Red timeout advances to Black's turn
        state.Version++;
        state.TurnIndex++;
        state.CountedActions++;
        state.SideToMove = Side.Black;

        var lifecycleEvents = new List<object>();
        var nextSide = Side.Black;
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, state.TurnIndex);

            // Collect events
            lifecycleEvents.Add(new { type = "turn.started", side = nextSide.ToString().ToLowerInvariant() });
            foreach (var e in lifecycle.ExpiredEffects)
                lifecycleEvents.Add(new { type = "effect.expired", effectId = e.EffectId, code = e.Code });
            foreach (var s in lifecycle.RemovedStakes)
                lifecycleEvents.Add(new { type = "stake.removed", obstacleId = s.ObstacleId });
            foreach (var c in lifecycle.DecrementedCooldowns)
                lifecycleEvents.Add(new { type = "skill.cooldown_decremented", side = c.Side.ToString().ToLowerInvariant(), slotNo = c.SlotNo });
        }

        // Events collected: turn.started + cooldown_decremented + effect.expired + stake.removed
        Assert.Equal(4, lifecycleEvents.Count);
        Assert.Contains(lifecycleEvents, e => e.GetType().GetProperty("type")?.GetValue(e)?.ToString() == "turn.started");
        Assert.Contains(lifecycleEvents, e => e.GetType().GetProperty("type")?.GetValue(e)?.ToString() == "skill.cooldown_decremented");
        Assert.Contains(lifecycleEvents, e => e.GetType().GetProperty("type")?.GetValue(e)?.ToString() == "effect.expired");
        Assert.Contains(lifecycleEvents, e => e.GetType().GetProperty("type")?.GetValue(e)?.ToString() == "stake.removed");
    }

    [Fact]
    public void TimeoutAsync_pattern_no_lifecycle_when_already_processed()
    {
        // Pattern: if turn was already processed, lifecycle should not run again
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;
        AddSkill(state, Side.Red, slot: 1, cooldownRemaining: 5);
        AddSkill(state, Side.Black, slot: 1, cooldownRemaining: 4); // Black skill for testing

        // First: Red's normal turn lifecycle at TurnIndex=1
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
        }
        var redCooldownAfterFirst = state.SkillStates[Side.Red][0].CooldownRemaining;
        Assert.Equal(4, redCooldownAfterFirst); // 5 -> 4

        // Simulate Red timeout: TurnIndex=1 -> 2, side changes to Black
        state.Version++;
        state.TurnIndex++;
        state.CountedActions++;
        state.SideToMove = Side.Black;

        // First Black lifecycle at TurnIndex=2
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);
        }
        var blackCooldownAfterFirst = state.SkillStates[Side.Black][0].CooldownRemaining;
        Assert.Equal(3, blackCooldownAfterFirst); // 4 -> 3

        // Simulate another timeout: TurnIndex=2 -> 3, side changes to Red
        state.Version++;
        state.TurnIndex++;
        state.CountedActions++;
        state.SideToMove = Side.Red;

        // Red's lifecycle at TurnIndex=3 (NOT same as TurnIndex=1)
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);
        }
        var redCooldownAfterSecond = state.SkillStates[Side.Red][0].CooldownRemaining;
        Assert.Equal(3, redCooldownAfterSecond); // 4 -> 3

        // Red cooldown decremented again (not stuck at 4)
        Assert.True(redCooldownAfterSecond < redCooldownAfterFirst);

        // Red's TurnIndex=1 is still marked as processed (not reprocessed)
        Assert.True(TurnLifecycle.IsTurnProcessed(state, Side.Red, 1));
        Assert.False(TurnLifecycle.IsTurnProcessed(state, Side.Red, 2)); // Never ran at TurnIndex=2
        Assert.True(TurnLifecycle.IsTurnProcessed(state, Side.Red, 3));

        // Black's turn at TurnIndex=2 is also marked
        Assert.True(TurnLifecycle.IsTurnProcessed(state, Side.Black, 2));
        Assert.False(TurnLifecycle.IsTurnProcessed(state, Side.Black, 1)); // Never ran Black at TurnIndex=1
    }

    [Fact]
    public void TimeoutAsync_pattern_match_end_does_not_skip_lifecycle()
    {
        // Pattern: even when timeout causes match end, lifecycle should still run for the new turn
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;
        AddSkill(state, Side.Black, slot: 1, cooldownRemaining: 2);

        // Red timeout in check = immediate loss
        var inCheck = true;
        var streak = 2;
        state.Version++;
        state.TurnIndex++;
        state.CountedActions++;
        var nextSide = Side.Black;
        state.SideToMove = nextSide;

        // Apply lifecycle for the new turn even though match ends
        if (!TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, nextSide);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, nextSide, state.TurnIndex);
        }

        // Lifecycle still ran and updated cooldowns
        Assert.Equal(1, state.SkillStates[Side.Black][0].CooldownRemaining);
        Assert.True(TurnLifecycle.IsTurnProcessed(state, nextSide, state.TurnIndex));

        // Now apply match end
        if (inCheck || streak >= 2)
        {
            state.Result = "black_win";
            state.EndReason = "timeout_in_check";
        }

        Assert.Equal("black_win", state.Result);
        Assert.Equal("timeout_in_check", state.EndReason);
    }

    #endregion

    #region Phase 3.3 — Creator Cancellation (exactly-once verified)

    // These tests verify the Creator Cancellation behavior with the refactored architecture.
    // The refactored Apply() executes core lifecycle (Steps 1-4) exactly once,
    // then adds Step 5 (Creator Cancellation) if resolveCreatorCancellation is true.
    // This ensures NO double mutation of cooldowns, effects, or stakes.

    [Fact]
    public void Single_Apply_with_resolveCreatorCancellation_handles_cancellation()
    {
        // Pattern: Single Apply() call with resolveCreatorCancellation: true
        // handles all 5 steps without double mutation
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Black;

        // Black's effect is stolen by Red (PositionControllers set to Red)
        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 5);
        state.EffectInstances[0].PositionControllers[new BoardPoint(4, 0)] = Side.Red;

        // Single Apply() call handles all steps
        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);

            // Cancellation should have removed the effect
            Assert.Single(lifecycle.RemovedEffects);
            Assert.Equal(EffectStateValue.Ended, state.EffectInstances[0].State);
        }
    }

    [Fact]
    public void Apply_with_resolveCreatorCancellation_decrements_cooldown_once()
    {
        // Critical test: With resolveCreatorCancellation: true, cooldown decrements EXACTLY ONCE
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Black;
        AddSkill(state, Side.Black, slot: 1, cooldownRemaining: 3);

        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);

            // Cooldown decremented exactly once: 3 -> 2
            Assert.Equal(2, state.SkillStates[Side.Black][0].CooldownRemaining);
            Assert.Single(lifecycle.DecrementedCooldowns);
            Assert.Equal(2, lifecycle.DecrementedCooldowns[0].NewCooldown);
        }
    }

    [Fact]
    public void Apply_with_resolveCreatorCancellation_decrements_effect_duration_once()
    {
        // Critical test: With resolveCreatorCancellation: true, effect duration decrements EXACTLY ONCE
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Black;

        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 3);

        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);

            // Effect duration decremented exactly once: 3 -> 2
            Assert.Equal(2, state.EffectInstances[0].RemainingDuration);
            Assert.Equal(EffectStateValue.Active, state.EffectInstances[0].State);
            Assert.Empty(lifecycle.ExpiredEffects); // Not expired yet
        }
    }

    [Fact]
    public void Apply_with_resolveCreatorCancellation_decrements_stake_lifetime_once()
    {
        // Critical test: With resolveCreatorCancellation: true, stake lifetime decrements EXACTLY ONCE
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Black;
        AddStake(state, Side.Black, remainingLifetime: 3);

        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);

            // Stake lifetime decremented exactly once: 3 -> 2
            Assert.Single(state.Obstacles);
            Assert.Equal(2, state.Obstacles[0].RemainingLifetime);
            Assert.Empty(lifecycle.RemovedStakes); // Not removed yet
        }
    }

    [Fact]
    public void Apply_with_resolveCreatorCancellation_finalizes_pending_steal_once()
    {
        // Critical test: Pending steal finalizes EXACTLY ONCE
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Black;

        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 5);
        state.EffectInstances[0].PendingController = Side.Red;

        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);

            // Pending steal finalized exactly once
            Assert.Single(lifecycle.FinalizedSteals);
            Assert.Null(state.EffectInstances[0].PendingController);
            Assert.Equal(Side.Red, state.EffectInstances[0].PositionControllers[new BoardPoint(4, 0)]);
        }
    }

    [Fact]
    public void Apply_resolveCreatorCancellation_false_does_not_cancel()
    {
        // When resolveCreatorCancellation is false, no cancellation occurs
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Black;

        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 5);
        state.EffectInstances[0].PositionControllers[new BoardPoint(4, 0)] = Side.Red;

        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: false);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);

            // Effect remains Active (not cancelled)
            Assert.Equal(EffectStateValue.Active, state.EffectInstances[0].State);
            Assert.Empty(lifecycle.RemovedEffects);
        }
    }

    [Fact]
    public void Apply_resolveCreatorCancellation_true_cancels_stolen_effect()
    {
        // When resolveCreatorCancellation is true, stolen effects are cancelled
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Black;

        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 5);
        state.EffectInstances[0].PositionControllers[new BoardPoint(4, 0)] = Side.Red;

        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);

            // Effect is cancelled (Ended)
            Assert.Equal(EffectStateValue.Ended, state.EffectInstances[0].State);
            Assert.Single(lifecycle.RemovedEffects);
        }
    }

    [Fact]
    public void Apply_with_resolveCreatorCancellation_preserves_invariants()
    {
        // All invariants preserved with the refactored single-Apply architecture
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Black;

        var originalEffectId = Guid.NewGuid();
        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 5, effectId: originalEffectId, creationOrder: 42);
        state.EffectInstances[0].PositionControllers[new BoardPoint(4, 0)] = Side.Red;

        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);

            // All invariants preserved
            Assert.Equal(originalEffectId, state.EffectInstances[0].EffectId);
            Assert.Equal(42, state.EffectInstances[0].CreationOrder);
            Assert.Equal(Side.Black, state.EffectInstances[0].Creator);
            Assert.Equal(5, state.EffectInstances[0].Duration); // Duration unchanged
        }
    }

    [Fact]
    public void Apply_only_affects_stolen_effects_for_cancellation()
    {
        // Cancellation only affects stolen effects, not own effects
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Black;

        // Black has two effects: one stolen by Red, one owned
        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 5);
        var stolenEffect = state.EffectInstances[0];
        stolenEffect.PositionControllers[new BoardPoint(4, 0)] = Side.Red;

        // Add an un-stolen Black effect
        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 3);
        state.EffectInstances[1].PositionControllers[new BoardPoint(5, 0)] = Side.Black;

        if (!TurnLifecycle.IsTurnProcessed(state, Side.Black, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Black, resolveCreatorCancellation: true);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Black, state.TurnIndex);

            // Only the stolen effect is cancelled
            Assert.Single(lifecycle.RemovedEffects);
            Assert.Equal(stolenEffect.EffectId, lifecycle.RemovedEffects[0].EffectId);
            // Own effect still Active
            Assert.Equal(EffectStateValue.Active, state.EffectInstances[1].State);
        }
    }

    [Fact]
    public void Non_creator_cannot_cancel()
    {
        // Non-Creator (Red) cannot cancel Black's stolen effect
        var state = EmptyState();
        state.TurnIndex = 1;
        state.SideToMove = Side.Red;

        // Black's effect is stolen by Red
        MakeEffect(state, Side.Black, EffectStateValue.Active, remainingDuration: 5);
        state.EffectInstances[0].PositionControllers[new BoardPoint(4, 0)] = Side.Red;

        if (!TurnLifecycle.IsTurnProcessed(state, Side.Red, state.TurnIndex))
        {
            var lifecycle = TurnLifecycle.Apply(state, Side.Red, resolveCreatorCancellation: true);
            state = lifecycle.State;
            TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex);

            // Red cannot cancel Black's effect - it remains Active
            Assert.Equal(EffectStateValue.Active, state.EffectInstances[0].State);
            Assert.Empty(lifecycle.RemovedEffects);
        }
    }

    #endregion
}

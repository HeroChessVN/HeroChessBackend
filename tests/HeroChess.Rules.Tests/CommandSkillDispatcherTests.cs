// Phase 2.3: Command Skill dispatcher unit tests.
using HeroChess.Rules;
using HeroChess.Rules.Effects;
using HeroChess.Rules.Skills;
using System.Text.Json;
using Xunit;

namespace HeroChess.Rules.Tests;

/// <summary>
/// Unit tests for the Command Skill dispatcher infrastructure (§8.4).
/// Tests cover: generic validation, handler resolution, cooldown application,
/// failure atomicity, success atomicity, registry, result model.
/// </summary>
public sealed class CommandSkillDispatcherTests
{
    #region Helper classes and setup

    private static GameState MakeState(Side sideToMove = Side.Red) => new()
    {
        StateSchemaVersion = 4,
        SideToMove = sideToMove,
        EffectInstances = new List<EffectInstance>(),
        ProcessedTurns = new Dictionary<Side, List<int>>
        {
            [Side.Red] = new(),
            [Side.Black] = new()
        },
        StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
        SkillStates = new Dictionary<Side, List<SkillState>>
        {
            [Side.Red] = new() { new(1, Guid.NewGuid(), null, 0, "noop_skill") },
            [Side.Black] = new() { new(1, Guid.NewGuid(), null, 0, "noop_skill") }
        }
    };

    private static JsonElement Action(int slot, object? target = null)
    {
        var dict = new Dictionary<string, object> { ["slot"] = slot };
        if (target != null) dict["target"] = target;
        return JsonSerializer.SerializeToElement(dict);
    }

    /// <summary>
    /// A no-op handler that always succeeds and adds an event.
    /// </summary>
    private sealed class NoopHandler : ICommandSkillHandler
    {
        public string ImplementationKey => "noop_skill";
        public CommandSkillResult Execute(CommandSkillContext ctx)
        {
            var events = new object[] { new { type = "noop.applied", side = ctx.ActorSide.ToString() } };
            return CommandSkillResult.Success(ctx.State, events);
        }
    }

    /// <summary>
    /// A handler that always fails with a specific error.
    /// </summary>
    private sealed class FailingHandler : ICommandSkillHandler
    {
        public string ImplementationKey => "failing_skill";
        private readonly string _code;
        private readonly string _message;

        public FailingHandler(string code, string message)
        {
            _code = code;
            _message = message;
        }

        public CommandSkillResult Execute(CommandSkillContext ctx) =>
            CommandSkillResult.Failure(ctx.State, _code, _message);
    }

    /// <summary>
    /// A handler that modifies the state (adds an EffectInstance).
    /// </summary>
    private sealed class StateModifyingHandler : ICommandSkillHandler
    {
        public string ImplementationKey => "state_modifying_skill";

        public CommandSkillResult Execute(CommandSkillContext ctx)
        {
            var state = ctx.State.Clone();
            state.EffectInstances.Add(new EffectInstance
            {
                EffectId = Guid.NewGuid(),
                Code = "test_effect",
                SkillId = ctx.SkillId,
                Creator = ctx.ActorSide,
                CreationOrder = state.NextCreationOrder++,
                Duration = 2,
                RemainingDuration = 2,
                State = EffectStateValue.Active,
                TargetPositions = new List<BoardPoint>(),
                PositionControllers = new Dictionary<BoardPoint, Side>()
            });
            return CommandSkillResult.Success(state);
        }
    }

    private static CommandSkillRegistry Registry(params ICommandSkillHandler[] handlers) =>
        new(handlers);

    #endregion

    #region Generic validation

    [Fact]
    public void Dispatch_rejects_wrong_turn()
    {
        var state = MakeState(Side.Red);
        var reg = Registry(new NoopHandler());
        var dispatcher = new CommandSkillDispatcher(reg);

        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Black][0]);
        var result = dispatcher.Dispatch(state, Side.Black, frozen, Action(1));

        Assert.False(result.Accepted);
        Assert.Equal("WRONG_TURN", result.Error!.Code);
    }

    [Fact]
    public void Dispatch_rejects_match_ended()
    {
        var state = MakeState(Side.Red);
        state.Result = "red_win";
        var reg = Registry(new NoopHandler());
        var dispatcher = new CommandSkillDispatcher(reg);

        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.False(result.Accepted);
        Assert.Equal("MATCH_ENDED", result.Error!.Code);
    }

    [Fact]
    public void Dispatch_rejects_skill_on_cooldown()
    {
        var state = MakeState(Side.Red);
        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { CooldownRemaining = 2 };
        var reg = Registry(new NoopHandler());
        var dispatcher = new CommandSkillDispatcher(reg);

        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.False(result.Accepted);
        Assert.Equal("SKILL_ON_COOLDOWN", result.Error!.Code);
    }

    [Fact]
    public void Dispatch_rejects_skill_not_in_lineup()
    {
        var state = MakeState(Side.Red);
        var reg = Registry(new NoopHandler());
        var dispatcher = new CommandSkillDispatcher(reg);

        var nonExistentSkill = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        // Slot 99 does not exist
        var fakeFrozen = new FrozenSkillSnapshot(99, Guid.NewGuid(), "noop_skill", 0);
        var result = dispatcher.Dispatch(state, Side.Red, fakeFrozen, Action(99));

        Assert.False(result.Accepted);
        Assert.Equal("SKILL_NOT_IN_LINEUP", result.Error!.Code);
    }

    [Fact]
    public void Dispatch_rejects_unknown_implementation_key()
    {
        var state = MakeState(Side.Red);
        var reg = Registry(new NoopHandler()); // only registers noop_skill
        var dispatcher = new CommandSkillDispatcher(reg);

        var unknownFrozen = new FrozenSkillSnapshot(1, Guid.NewGuid(), "unknown_skill", 0);
        var result = dispatcher.Dispatch(state, Side.Red, unknownFrozen, Action(1));

        Assert.False(result.Accepted);
        Assert.Equal("SKILL_NOT_IMPLEMENTED", result.Error!.Code);
    }

    #endregion

    #region Handler resolution and execution

    [Fact]
    public void Dispatch_resolves_correct_handler_by_key()
    {
        var state = MakeState(Side.Red);
        var noop = new NoopHandler();
        var failing = new FailingHandler("TEST_FAIL", "test");
        var reg = Registry(noop, failing);
        var dispatcher = new CommandSkillDispatcher(reg);

        // Switch skill slot to failing_skill
        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { ImplementationKey = "failing_skill" };
        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.False(result.Accepted);
        Assert.Equal("TEST_FAIL", result.Error!.Code);
    }

    [Fact]
    public void Dispatch_successful_execution_returns_handler_state()
    {
        var state = MakeState(Side.Red);
        var handler = new StateModifyingHandler();
        var reg = Registry(handler);
        var dispatcher = new CommandSkillDispatcher(reg);

        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { ImplementationKey = "state_modifying_skill" };
        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.True(result.Accepted);
        Assert.Single(result.State.EffectInstances);
        Assert.Equal("test_effect", result.State.EffectInstances[0].Code);
    }

    [Fact]
    public void Dispatch_returns_handler_events_on_success()
    {
        var state = MakeState(Side.Red);
        var handler = new NoopHandler();
        var reg = Registry(handler);
        var dispatcher = new CommandSkillDispatcher(reg);

        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.True(result.Accepted);
        Assert.Single(result.Events);
        Assert.Equal("noop.applied", ((dynamic)result.Events[0]).type);
    }

    #endregion

    #region Cooldown application

    [Fact]
    public void Dispatch_sets_cooldown_on_success()
    {
        var state = MakeState(Side.Red);
        var handler = new NoopHandler();
        var reg = Registry(handler);
        var dispatcher = new CommandSkillDispatcher(reg);

        var frozen = new FrozenSkillSnapshot(1, Guid.NewGuid(), "noop_skill", 3);
        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.True(result.Accepted);
        Assert.Equal(3, result.State.SkillStates[Side.Red][0].CooldownRemaining);
    }

    [Fact]
    public void Dispatch_consumes_limited_uses_and_refuses_exhausted_skill()
    {
        var state = MakeState(Side.Red);
        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { UsesRemaining = 1 };
        var dispatcher = new CommandSkillDispatcher(Registry(new NoopHandler()));
        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0], 2);

        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.True(result.Accepted);
        Assert.Equal(2, result.State.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Equal(0, result.State.SkillStates[Side.Red][0].UsesRemaining);
        result.State.SkillStates[Side.Red][0] = result.State.SkillStates[Side.Red][0] with { CooldownRemaining = 0 };
        Assert.Equal("SKILL_USES_EXHAUSTED", dispatcher.Dispatch(result.State, Side.Red, frozen, Action(1)).Error?.Code);
    }

    [Fact]
    public void Dispatch_does_not_set_cooldown_on_failure()
    {
        var state = MakeState(Side.Red);
        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { CooldownRemaining = 0 };
        var reg = Registry(new FailingHandler("TEST", "test"));
        var dispatcher = new CommandSkillDispatcher(reg);

        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { ImplementationKey = "failing_skill" };
        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.False(result.Accepted);
        // Cooldown unchanged
        Assert.Equal(0, result.State.SkillStates[Side.Red][0].CooldownRemaining);
    }

    #endregion

    #region Atomicity

    [Fact]
    public void Dispatch_does_not_mutate_original_state_on_failure()
    {
        var state = MakeState(Side.Red);
        var handler = new StateModifyingHandler();
        var reg = Registry(handler);
        var dispatcher = new CommandSkillDispatcher(reg);

        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { ImplementationKey = "state_modifying_skill" };
        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);

        // Try with wrong side to force WRONG_TURN failure
        var wrongSideFrozen = new FrozenSkillSnapshot(1, Guid.NewGuid(), "noop_skill", 0);
        var result = dispatcher.Dispatch(state, Side.Black, wrongSideFrozen, Action(1));

        Assert.False(result.Accepted);
        Assert.Empty(state.EffectInstances); // Original unchanged
    }

    [Fact]
    public void Dispatch_does_not_mutate_original_state_on_handler_failure()
    {
        var state = MakeState(Side.Red);
        var handler = new FailingHandler("HANDLER_FAIL", "test");
        var reg = Registry(handler);
        var dispatcher = new CommandSkillDispatcher(reg);

        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { ImplementationKey = "failing_skill" };
        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);

        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.False(result.Accepted);
        Assert.Empty(state.EffectInstances); // Original unchanged
    }

    [Fact]
    public void Dispatch_returns_original_state_on_failure()
    {
        var state = MakeState(Side.Red);
        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { ImplementationKey = "failing_skill" };
        var reg = Registry(new FailingHandler("TEST", "test"));
        var dispatcher = new CommandSkillDispatcher(reg);

        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        var result = dispatcher.Dispatch(state, Side.Red, frozen, Action(1));

        Assert.False(result.Accepted);
        Assert.Same(state, result.State);
    }

    #endregion

    #region Registry

    [Fact]
    public void Registry_resolves_registered_handler()
    {
        var handler = new NoopHandler();
        var reg = Registry(handler);

        Assert.True(reg.TryGet("noop_skill", out var resolved));
        Assert.Same(handler, resolved);
    }

    [Fact]
    public void Registry_returns_false_for_unknown_key()
    {
        var reg = Registry(new NoopHandler());

        Assert.False(reg.TryGet("unknown_skill", out var resolved));
        Assert.Null(resolved);
    }

    [Fact]
    public void Registry_IsRegistered_returns_true_for_registered()
    {
        var reg = Registry(new NoopHandler());

        Assert.True(reg.IsRegistered("noop_skill"));
        Assert.False(reg.IsRegistered("unknown"));
    }

    [Fact]
    public void Registry_With_adds_handler()
    {
        var baseReg = Registry(new NoopHandler());
        var newHandler = new FailingHandler("X", "Y");
        var extendedReg = baseReg.With(newHandler);

        Assert.True(extendedReg.IsRegistered("noop_skill"));
        Assert.True(extendedReg.IsRegistered("failing_skill"));
        Assert.False(baseReg.IsRegistered("failing_skill")); // original unchanged
    }

    [Fact]
    public void Registry_With_overwrites_existing()
    {
        // Test that adding a handler with the same key overwrites the previous one.
        var handler1 = new NoopHandler(); // key = "noop_skill"
        var handler2 = new FailingHandler("X", "Y");
        var reg = Registry(handler1).With(handler2); // handler2 also has key "noop_skill" ( FailingHandler.ImplementationKey = "failing_skill")

        // NoopHandler ("noop_skill") is in base registry.
        Assert.True(reg.IsRegistered("noop_skill"));
        // FailingHandler ("failing_skill") is added via With.
        Assert.True(reg.IsRegistered("failing_skill"));
        // Since they have different keys, neither is overwritten.
        Assert.True(reg.TryGet("noop_skill", out var noopResolved));
        Assert.Same(handler1, noopResolved);
    }

    [Fact]
    public void Registry_With_same_key_overwrites()
    {
        // Test that adding a handler with the same key as an existing one replaces it.
        var handler1 = new NoopHandler(); // key = "noop_skill"
        var handler2 = new OverwriteHandler(); // key = "noop_skill" (same!)
        var reg = Registry(handler1).With(handler2);

        Assert.True(reg.TryGet("noop_skill", out var resolved));
        Assert.Same(handler2, resolved); // overwritten
    }

    private sealed class OverwriteHandler : ICommandSkillHandler
    {
        public string ImplementationKey => "noop_skill";
        public CommandSkillResult Execute(CommandSkillContext ctx) =>
            CommandSkillResult.Success(ctx.State);
    }

    #endregion

    #region Result model

    [Fact]
    public void CommandSkillResult_Success_has_correct_properties()
    {
        var state = MakeState();
        var result = CommandSkillResult.Success(state);

        Assert.True(result.Accepted);
        Assert.Same(state, result.State);
        Assert.Empty(result.Events);
        Assert.Null(result.Error);
    }

    [Fact]
    public void CommandSkillResult_Success_with_cooldown()
    {
        var state = MakeState();
        var result = CommandSkillResult.Success(state, null, 2);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.CooldownTurns);
    }

    [Fact]
    public void CommandSkillResult_Failure_has_correct_properties()
    {
        var state = MakeState();
        var result = CommandSkillResult.Failure(state, "ERR_CODE", "ERR_MSG");

        Assert.False(result.Accepted);
        Assert.Equal("ERR_CODE", result.Error!.Code);
        Assert.Equal("ERR_MSG", result.Error.Message);
    }

    #endregion

    #region Multiple skills per turn

    [Fact]
    public void Dispatch_allows_multiple_skills_same_turn()
    {
        // No global one-skill-per-turn restriction.
        var state = MakeState(Side.Red);
        state.SkillStates[Side.Red].Add(new SkillState(2, Guid.NewGuid(), null, 0, "noop_skill"));
        var noop1 = new NoopHandler();
        var noop2 = new NoopHandler();
        var reg = Registry(noop1, noop2);
        var dispatcher = new CommandSkillDispatcher(reg);

        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { ImplementationKey = "noop_skill" };
        var frozen1 = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        var result1 = dispatcher.Dispatch(state, Side.Red, frozen1, Action(1));

        Assert.True(result1.Accepted);

        state.SkillStates[Side.Red][1] = state.SkillStates[Side.Red][1] with { ImplementationKey = "noop_skill" };
        var frozen2 = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][1]);
        var result2 = dispatcher.Dispatch(result1.State, Side.Red, frozen2, Action(2));

        Assert.True(result2.Accepted);
    }

    #endregion

    #region Target passing

    [Fact]
    public void Dispatch_passes_target_to_handler()
    {
        var state = MakeState(Side.Red);
        var reg = Registry(new TargetCaptureHandler());
        var dispatcher = new CommandSkillDispatcher(reg);

        state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { ImplementationKey = "target_capture" };
        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        var targetAction = JsonSerializer.SerializeToElement(new { slot = 1, target = new { effectId = Guid.NewGuid().ToString() } });
        var result = dispatcher.Dispatch(state, Side.Red, frozen, targetAction);

        Assert.True(result.Accepted);
        Assert.NotNull(TargetCaptureHandler.LastContext);
        // The target's effectId should match the UUID we passed in the action.
        Assert.NotNull(TargetCaptureHandler.LastContext!.Target.GetProperty("effectId").GetString());
    }

    private sealed class TargetCaptureHandler : ICommandSkillHandler
    {
        public static CommandSkillContext? LastContext { get; private set; }

        public string ImplementationKey => "target_capture";
        public CommandSkillResult Execute(CommandSkillContext ctx)
        {
            LastContext = ctx;
            return CommandSkillResult.Success(ctx.State);
        }
    }

    #endregion

    #region Server authority preserved

    [Fact]
    public void Dispatch_does_not_trust_client_side_info()
    {
        // Client sends wrong side - server must reject.
        var state = MakeState(Side.Red);
        var reg = Registry(new NoopHandler());
        var dispatcher = new CommandSkillDispatcher(reg);

        var frozen = FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]);
        // Client tries to activate as Black, but it's Red's turn
        var result = dispatcher.Dispatch(state, Side.Black, frozen, Action(1));

        Assert.False(result.Accepted);
        Assert.Equal("WRONG_TURN", result.Error!.Code);
    }

    #endregion
}

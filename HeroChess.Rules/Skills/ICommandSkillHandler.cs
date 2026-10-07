// Phase 2.3: Command Skill handler infrastructure.
// No gameplay logic; only the handler contract and execution infrastructure.

namespace HeroChess.Rules.Skills;

/// <summary>
/// Result of a Command Skill handler execution.
/// Success includes the updated state, any events to record, and the cooldown to apply.
/// Failure means no state was changed and no cooldown was consumed.
///
/// Design rationale:
/// - Returning a result (rather than throwing) cleanly separates validation failures from system errors.
/// - Validation failures (bad target, wrong state) are returned as failures, not exceptions.
/// - Only unexpected system errors should throw.
/// </summary>
public sealed class CommandSkillResult
{
    private CommandSkillResult(bool accepted, GameState state, IReadOnlyList<object>? events, int? cooldownTurns, RuleError? error)
    {
        Accepted = accepted;
        State = state;
        Events = events ?? Array.Empty<object>();
        CooldownTurns = cooldownTurns;
        Error = error;
    }

    /// <summary>True if the skill executed successfully.</summary>
    public bool Accepted { get; }

    /// <summary>
    /// The resulting game state. If Accepted == true this is the new authoritative state.
    /// If Accepted == false this is the original state (no mutation occurred).
    /// </summary>
    public GameState State { get; }

    /// <summary>Events to record in match_action.ResolvedEvents.</summary>
    public IReadOnlyList<object> Events { get; }

    /// <summary>
    /// Cooldown turns to set on the skill's slot. Null means no cooldown change.
    /// Only set on Accepted == true. Failed executions do not consume cooldown.
    /// </summary>
    public int? CooldownTurns { get; }

    /// <summary>Error details when Accepted == false.</summary>
    public RuleError? Error { get; }

    /// <summary>Creates a successful result with updated state, events, and cooldown.</summary>
    /// <param name="state">The new authoritative state.</param>
    /// <param name="events">Events to record.</param>
    /// <param name="cooldownTurns">Cooldown to apply (null = no cooldown).</param>
    public static CommandSkillResult Success(GameState state, IReadOnlyList<object>? events = null, int? cooldownTurns = null) =>
        new(true, state, events, cooldownTurns, null);

    /// <summary>Creates a failure result. No state is mutated; no cooldown is consumed.</summary>
    /// <param name="state">The original state (unchanged).</param>
    /// <param name="code">Error code string.</param>
    /// <param name="message">Human-readable error message.</param>
    public static CommandSkillResult Failure(GameState state, string code, string message) =>
        new(false, state, null, null, new RuleError(code, message));
}

/// <summary>
/// Context passed to a Command Skill handler during execution.
/// Provides access to all information the handler legitimately needs.
///
/// The handler must NOT directly mutate the state from this context.
/// Instead, return a CommandSkillResult with the updated state.
///
/// Design rationale:
/// - Keep context immutable during execution (no risk of handler bypassing validation).
/// - Provide frozen skill snapshot (Immutability: the SkillId/SlotNo/ImplementationKey cannot change mid-execution).
/// - Provide current state (only read; mutation via return value).
/// - Provide turn info (for exact timing rules).
/// </summary>
public sealed class CommandSkillContext
{
    public CommandSkillContext(
        GameState state,
        Side actorSide,
        int slotNo,
        Guid skillId,
        string implementationKey,
        int? cooldownTurns,
        System.Text.Json.JsonElement target)
    {
        State = state;
        ActorSide = actorSide;
        SlotNo = slotNo;
        SkillId = skillId;
        ImplementationKey = implementationKey;
        CooldownTurns = cooldownTurns;
        Target = target;
    }

    /// <summary>The current authoritative game state (read-only; mutation via return value).</summary>
    public GameState State { get; }

    /// <summary>The side that activated the skill.</summary>
    public Side ActorSide { get; }

    /// <summary>The slot number of the activated skill.</summary>
    public int SlotNo { get; }

    /// <summary>The unique identifier of the skill (from FrozenSkill).</summary>
    public Guid SkillId { get; }

    /// <summary>The implementation key that maps to a handler (e.g. "van_coc_tran_giang").</summary>
    public string ImplementationKey { get; }

    /// <summary>The default cooldown turns from the skill catalog (from FrozenSkill).</summary>
    public int? CooldownTurns { get; }

    /// <summary>The target from the client action payload (JsonElement for flexibility).</summary>
    public System.Text.Json.JsonElement Target { get; }
}

/// <summary>
/// Contract for a Command Skill handler.
///
/// Responsibilities:
/// 1. Validate whether the skill can execute given the current state and target.
/// 2. If valid: apply the state change and return a successful result.
/// 3. If invalid: return a failure result without mutating state.
///
/// The handler must NOT:
/// - Throw for expected validation failures (return Failure instead).
/// - Directly mutate the context's State (return a new state instead).
/// - Bypass server authority (all validation must be server-side).
///
/// The handler CAN:
/// - Read any part of the context's State.
/// - Create EffectInstances using EffectFactory.
/// - Modify PositionControllers, State, RemainingDuration via the returned GameState.
/// - Access payload metadata for terrain/path logic.
///
/// Thread-safety: Handlers are registered once at startup and are stateless
/// (or use only constructor-injected dependencies). Execution is serialized by the
/// caller's lock (MatchCommandService holds the match lock during execution).
/// </summary>
public interface ICommandSkillHandler
{
    /// <summary>
    /// The implementation key this handler serves (must match FrozenSkill.ImplementationKey).
    /// </summary>
    string ImplementationKey { get; }

    /// <summary>
    /// Validates whether the skill can execute and applies the state change.
    ///
    /// Validation steps (handler order):
    /// 1. Check skill is not currently Disabled (if the skill supports disabled state).
    /// 2. Validate the target per skill-specific rules.
    /// 3. Apply the skill effect to the state.
    /// 4. Return Success with the new state and cooldown.
    ///
    /// If validation fails at any step, return Failure(state, code, message).
    /// The context's State must NOT be mutated on a failure return.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <returns>A result indicating success or failure with details.</returns>
    CommandSkillResult Execute(CommandSkillContext context);
}

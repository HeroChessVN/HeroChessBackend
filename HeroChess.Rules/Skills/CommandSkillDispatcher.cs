// Phase 2.3: Frozen Skill snapshot for Command Skill dispatcher.
// This is a lightweight snapshot of skill metadata needed by the dispatcher.
// It lives in HeroChess.Rules to avoid a dependency on HeroChess.Api types.

using System.Text.Json;

namespace HeroChess.Rules.Skills;

/// <summary>
/// A frozen snapshot of skill metadata for a Command Skill.
/// This is a lightweight DTO (not an EF entity) used only by the dispatcher.
/// </summary>
public sealed record FrozenSkillSnapshot(
    int SlotNo,
    Guid SkillId,
    string ImplementationKey,
    int CooldownRemaining);

/// <summary>
/// Creates a FrozenSkillSnapshot from a SkillState record (for the dispatcher).
/// </summary>
public static class FrozenSkillSnapshotFactory
{
    public static FrozenSkillSnapshot FromSkillState(SkillState skill) =>
        new(skill.SlotNo, skill.SkillId, skill.ImplementationKey ?? "", skill.CooldownRemaining);
}

/// <summary>
/// The Command Skill dispatcher coordinates skill execution:
/// 1. Resolves the FrozenSkill for the actor's lineup slot.
/// 2. Checks generic preconditions (correct player, cooldown, etc.).
/// 3. Resolves the ICommandSkillHandler for the ImplementationKey.
/// 4. Builds a CommandSkillContext and calls handler.Execute().
/// 5. On success: applies cooldown to the result state.
/// 6. Returns the result to the caller for commit.
///
/// Atomicity contract:
/// - If the dispatcher returns Accepted == false: no state was mutated.
/// - If the dispatcher returns Accepted == true: state contains the skill effect and cooldown.
/// - The caller is responsible for committing the result atomically.
///
/// Thread-safety: caller holds the match lock.
///
/// Generic validation rules enforced by the dispatcher (not delegated to handlers):
/// - Actor is the player whose turn it is.
/// - Skill slot is valid for the actor's lineup.
/// - Skill cooldown is 0.
/// - Match is active (no result yet).
/// </summary>
public sealed class CommandSkillDispatcher
{
    private readonly CommandSkillRegistry _registry;

    public CommandSkillDispatcher(CommandSkillRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Attempts to dispatch a team_skill action.
    ///
    /// Generic validation is applied first. If any generic check fails, returns a failure
    /// without calling the handler. On success, calls the handler which may still return
    /// a failure (e.g., invalid target, wrong game state).
    ///
    /// Returns a CommandSkillResult where:
    /// - Accepted == true: state is the new authoritative state, cooldown applied, caller commits.
    /// - Accepted == false: state is the original state (unchanged), caller rolls back.
    /// </summary>
    /// <param name="state">Current game state (will be passed to handler on success).</param>
    /// <param name="actorSide">The Side attempting to activate the skill.</param>
    /// <param name="frozenSkill">The resolved skill metadata for the slot being activated.</param>
    /// <param name="action">The raw action JsonElement from the client.</param>
    /// <returns>A result with either the new state or an error.</returns>
    public CommandSkillResult Dispatch(
        GameState state,
        Side actorSide,
        FrozenSkillSnapshot frozenSkill,
        JsonElement action)
    {
        // --- Generic preconditions ---

        // 1. It must be this player's turn.
        if (state.SideToMove != actorSide)
            return CommandSkillResult.Failure(state, "WRONG_TURN",
                "It is not this participant's turn.");

        // 2. Match must be active (no result yet).
        if (state.Result is not null)
            return CommandSkillResult.Failure(state, "MATCH_ENDED",
                "The match has already ended.");

        // 3. Skill cooldown must be 0.
        var skillState = FindSkillState(state, actorSide, frozenSkill.SlotNo);
        if (skillState == null)
            return CommandSkillResult.Failure(state, "SKILL_NOT_IN_LINEUP",
                $"No skill found in slot {frozenSkill.SlotNo} for this lineup.");
        if (skillState.CooldownRemaining > 0)
            return CommandSkillResult.Failure(state, "SKILL_ON_COOLDOWN",
                $"Skill is on cooldown for {skillState.CooldownRemaining} more turn(s).");

        // 4. Resolve handler by ImplementationKey.
        if (!_registry.TryGet(frozenSkill.ImplementationKey, out var rawHandler) || rawHandler == null)
            return CommandSkillResult.Failure(state, "SKILL_NOT_IMPLEMENTED",
                $"The skill '{frozenSkill.ImplementationKey}' is not implemented.");
        var handler = rawHandler;

        // --- Extract target from action ---
        JsonElement target;
        if (action.TryGetProperty("target", out var targetProp))
            target = targetProp;
        else
            target = default(JsonElement); // empty/default element

        // --- Build context ---
        var context = new CommandSkillContext(
            state: state,
            actorSide: actorSide,
            slotNo: frozenSkill.SlotNo,
            skillId: frozenSkill.SkillId,
            implementationKey: frozenSkill.ImplementationKey,
            cooldownTurns: frozenSkill.CooldownRemaining,
            target: target);

        // --- Execute handler ---
        var result = handler.Execute(context);

        // --- On failure: return failure immediately (no state mutation, no cooldown) ---
        if (!result.Accepted)
            return CommandSkillResult.Failure(state, result.Error!.Code, result.Error!.Message);

        // --- On success: apply cooldown to the result state ---
        var newState = result.State;
        var updatedSkill = FindSkillState(newState, actorSide, frozenSkill.SlotNo);
        if (updatedSkill != null)
        {
            // Replace with new cooldown (SkillState is a record with init-only CooldownRemaining).
            var updatedList = newState.SkillStates[actorSide];
            for (var i = 0; i < updatedList.Count; i++)
            {
                if (updatedList[i].SlotNo == frozenSkill.SlotNo)
                {
                    updatedList[i] = updatedList[i] with { CooldownRemaining = frozenSkill.CooldownRemaining };
                    break;
                }
            }
        }

        return CommandSkillResult.Success(newState, result.Events, frozenSkill.CooldownRemaining);
    }

    private static SkillState? FindSkillState(GameState state, Side side, int slotNo)
    {
        if (!state.SkillStates.TryGetValue(side, out var list)) return null;
        return list.FirstOrDefault(s => s.SlotNo == slotNo);
    }
}

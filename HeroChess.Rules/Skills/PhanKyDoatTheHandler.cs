// Phase 2.4.2: Phản Kỳ Đoạt Thế Command Skill handler.
using HeroChess.Rules.Effects;

namespace HeroChess.Rules.Skills;

/// <summary>
/// Handler for Phản Kỳ Đoạt Thế.
///
/// Purpose: Transfer control of an eligible opponent-created terrain Effect.
///
/// What this skill does NOT do:
/// - Does NOT create a new EffectId.
/// - Does NOT change Creator (remains original).
/// - Does NOT change CreationOrder.
/// - Does NOT reset duration.
/// - Does NOT affect Disabled Effects.
///
/// Only Active terrain Effects are stealable.
///
/// CONFIRMED TIMING (pending/deferred transfer):
/// - Steal is PENDING at the time of use.
/// - Official PositionController does NOT immediately change.
/// - PendingController is set to the actor Side.
/// - Original Creator remains in control until end of current turn.
/// - At the end of the current turn, PendingController is finalized:
///   - PositionController changes to PendingController Side.
///   - PendingController is cleared.
/// - The original Creator can remove the steal effect during their NEXT turn
///   (this is removal of the effect, not a "steal back").
/// - Duration, Creator, EffectId, CreationOrder remain unchanged throughout.
/// </summary>
public sealed class PhanKyDoatTheHandler : ICommandSkillHandler
{
    public string ImplementationKey => SkillKeys.PhanKyDoatThe;

    public CommandSkillResult Execute(CommandSkillContext ctx)
    {
        // --- Parse target ---
        var (effectId, position) = RiverGeometry.TryParseEffectIdAndPosition(ctx.Target);
        if (!effectId.HasValue)
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET",
                "Phản Kỳ requires a target with effectId and position.");

        // --- Find the target Effect ---
        if (!ctx.State.TryGetEffect(effectId.Value, out var effect))
            return CommandSkillResult.Failure(ctx.State, "EFFECT_NOT_FOUND",
                $"Effect '{effectId}' was not found.");

        // --- Validate: must be Active (not Disabled, not Ended) ---
        // U6 = A: only Active Effects are stealable
        if (effect.State == EffectStateValue.Disabled)
            return CommandSkillResult.Failure(ctx.State, "EFFECT_DISABLED",
                "Phản Kỳ cannot target a Disabled Effect.");

        if (effect.State == EffectStateValue.Ended)
            return CommandSkillResult.Failure(ctx.State, "EFFECT_ENDED",
                "The Effect has already ended and cannot be stolen.");

        // --- Validate: opponent-created Effect ---
        if (effect.Creator == ctx.ActorSide)
            return CommandSkillResult.Failure(ctx.State, "CANNOT_STEAL_OWN_EFFECT",
                "Phản Kỳ cannot target your own Effect.");

        // --- Validate: only terrain-affecting Effects are stealable (U7 = A) ---
        // Check payload for terrain kind
        if (!effect.Payload.TryGetValue("kind", out var kindVal) ||
            !string.Equals(kindVal as string, "terrain", StringComparison.Ordinal))
            return CommandSkillResult.Failure(ctx.State, "EFFECT_NOT_STEALABLE",
                "Phản Kỳ can only target terrain-affecting Effects.");

        // --- Validate: position is in the Effect's TargetPositions ---
        if (position.HasValue && !effect.TargetPositions.Contains(position.Value))
            return CommandSkillResult.Failure(ctx.State, "INVALID_POSITION",
                $"Position ({position.Value.X},{position.Value.Y}) is not part of the target Effect.");

        // --- Validate: position exists in PositionControllers ---
        if (position.HasValue && !effect.PositionControllers.ContainsKey(position.Value))
            return CommandSkillResult.Failure(ctx.State, "POSITION_NOT_FOUND",
                "The specified position is not in the Effect's PositionControllers.");

        // --- Apply PENDING steal (not immediate transfer) ---
        var next = ctx.State.Clone();
        var clonedEffect = next.EffectInstances.Single(e => e.EffectId == effectId.Value);

        // Set pending controller (for specific position or all positions)
        if (position.HasValue)
        {
            // For position-specific steal, we store the pending controller
            // The actual position-level tracking would need separate handling
            // For simplicity, we set PendingController at effect level
            clonedEffect.PendingController = ctx.ActorSide;
        }
        else
        {
            // Transfer all positions - set pending controller
            clonedEffect.PendingController = ctx.ActorSide;
        }

        // --- Emit pending_steal event ---
        var events = new object[]
        {
            new { 
                type = "phan_ky.pending_steal", 
                effectId = effectId.Value, 
                pendingController = ctx.ActorSide.ToString().ToLowerInvariant(),
                message = "Steal is pending until end of current turn"
            }
        };

        return CommandSkillResult.Success(next, events);
    }
}

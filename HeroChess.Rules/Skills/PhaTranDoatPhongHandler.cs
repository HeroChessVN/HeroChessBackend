// Phase 2.4.3: Phá Trận Đoạt Phong Command Skill handler.
using HeroChess.Rules.Effects;

namespace HeroChess.Rules.Skills;

/// <summary>
/// Handler for Phá Trận Đoạt Phong.
///
/// Purpose: Reduce the remaining duration of an opponent-created Effect
/// assigned to one of the user's pieces.
///
/// A4 Resolution — Effect→Piece assignment representation:
/// The Effect's Payload carries an "assignedPieceId" field identifying which
/// of the user's pieces this Effect is assigned to.
///
/// This is the smallest architecture change (payload field only) and does not
/// require DB/API changes. The Payload is a Dictionary on EffectInstance.
///
/// Validation:
/// 1. Target Effect exists.
/// 2. Target Effect was created by the opponent.
/// 3. Target Effect is assigned to one of the actor's alive pieces.
/// 4. Target Effect has at least 1 remaining turn (RemainingDuration > 0).
/// 5. Target Effect is not Ended.
///
/// Duration reduction:
/// - Reduces RemainingDuration by exactly 1.
/// - If RemainingDuration reaches 0, Effect transitions to Ended immediately.
/// - Can affect Disabled Effects (duration continues for Disabled per U-DUR = A).
///
/// What this skill does NOT do:
/// - Does NOT create a new Effect.
/// - Does NOT change Creator.
/// - Does NOT change CreationOrder.
/// - Does NOT reset duration.
/// - Does NOT change PositionControllers.
/// </summary>
public sealed class PhaTranDoatPhongHandler : ICommandSkillHandler
{
    public string ImplementationKey => SkillKeys.PhaTranDoatPhong;

    public CommandSkillResult Execute(CommandSkillContext ctx)
    {
        // --- Parse target ---
        var effectId = RiverGeometry.TryParseEffectId(ctx.Target);
        if (!effectId.HasValue)
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET",
                "Phá Trận requires a target with an effectId.");

        // --- Find the target Effect ---
        if (!ctx.State.TryGetEffect(effectId.Value, out var effect))
            return CommandSkillResult.Failure(ctx.State, "EFFECT_NOT_FOUND",
                $"Effect '{effectId}' was not found.");

        // --- Validate: Effect must not be Ended ---
        if (effect.State == EffectStateValue.Ended)
            return CommandSkillResult.Failure(ctx.State, "EFFECT_ENDED",
                "The Effect has already ended.");

        // --- Validate: opponent-created Effect ---
        if (effect.Creator == ctx.ActorSide)
            return CommandSkillResult.Failure(ctx.State, "CANNOT_TARGET_OWN_EFFECT",
                "Phá Trận cannot target your own Effect.");

        // --- A4: Validate Effect is assigned to one of the actor's alive pieces ---
        // Read from Effect.Payload["assignedPieceId"]
        if (!effect.Payload.TryGetValue("assignedPieceId", out var assignedPieceIdVal) ||
            assignedPieceIdVal is not Guid assignedPieceId)
            return CommandSkillResult.Failure(ctx.State, "EFFECT_NOT_ASSIGNED",
                "The Effect is not assigned to any of your pieces.");

        var assignedPiece = ctx.State.Pieces.FirstOrDefault(p =>
            p.PieceId == assignedPieceId && p.Side == ctx.ActorSide && p.Status == PieceStatus.Alive);

        if (assignedPiece == null)
            return CommandSkillResult.Failure(ctx.State, "ASSIGNED_PIECE_NOT_FOUND",
                "The Effect's assigned piece is not currently alive or does not belong to you.");

        // --- Validate: Effect must have at least 1 remaining turn ---
        if (effect.RemainingDuration <= 0)
            return CommandSkillResult.Failure(ctx.State, "EFFECT_EXPIRED",
                "The Effect has no remaining duration to reduce.");

        // --- Apply duration reduction ---
        var next = ctx.State.Clone();
        var clonedEffect = next.EffectInstances.Single(e => e.EffectId == effectId.Value);
        var oldDuration = clonedEffect.RemainingDuration;
        clonedEffect.RemainingDuration -= 1;

        var events = new List<object>();
        if (clonedEffect.RemainingDuration == 0)
        {
            // Effect ends immediately when duration reaches 0.
            clonedEffect.State = EffectStateValue.Ended;
            events.Add(new
            {
                type = "effect.ended",
                effectId = effectId.Value,
                cause = "duration_reduced_to_zero",
                positions = clonedEffect.TargetPositions.Select(p => new { p.X, p.Y }).ToArray()
            });
        }
        else
        {
            events.Add(new
            {
                type = "effect.duration_reduced",
                effectId = effectId.Value,
                from = oldDuration,
                to = clonedEffect.RemainingDuration
            });
        }

        events.Add(new
        {
            type = "pha_tran.reduced",
            effectId = effectId.Value,
            newRemainingDuration = clonedEffect.RemainingDuration,
            assignedPieceId = assignedPieceId
        });

        return CommandSkillResult.Success(next, events);
    }
}

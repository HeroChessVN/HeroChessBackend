// Phase 2.4.4: Binh Lâm Thủy Hiểm Command Skill handler.
using HeroChess.Rules.Effects;

namespace HeroChess.Rules.Skills;

/// <summary>
/// Handler for Binh Lâm Thủy Hiểm.
///
/// Purpose: Restrict selected river-crossing routes.
///
/// Activation selects 3 consecutive river-crossing paths (3 consecutive columns per U-GEO = A).
///
/// Partial application (U8 = A):
/// - If any selected path conflicts with an existing terrain Effect, that path is excluded.
/// - Non-conflicting paths still receive Binh Lâm.
/// - One activation = one root Effect.
/// - If ALL selected paths conflict → activation fails atomically (no Effect created, no cooldown).
///
/// Duration: 1 player turn of the user.
/// The Effect expires before the user may perform an action in the next turn.
///
/// This Skill does NOT modify Xiangqi movement rules globally.
/// The Effect exists in state; movement rules engine checks it during legal-move generation.
///
/// What this skill does NOT do:
/// - Does NOT create separate Effects per path.
/// - Does NOT split the root Effect.
/// - Does NOT change PositionControllers for already-transferred positions.
/// </summary>
public sealed class BinhLamThuyHienHandler : ICommandSkillHandler
{
    public string ImplementationKey => SkillKeys.BinhLamThuyHien;

    public CommandSkillResult Execute(CommandSkillContext ctx)
    {
        // --- Parse target ---
        var paths = RiverGeometry.TryParsePaths(ctx.Target);
        if (paths == null || paths.Length != 3)
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET",
                "Binh Lâm requires a target with exactly 3 river path column indices.");

        // --- Validate 3 consecutive river columns per U-GEO = A ---
        Array.Sort(paths);
        if (!RiverGeometry.IsValidRiverPathSelection(paths))
            return CommandSkillResult.Failure(ctx.State, "INVALID_RIVER_PATH",
                "Binh Lâm requires exactly 3 consecutive river columns within {4, 5, 6}.");

        // --- Resolve non-conflicting paths (U8 = A partial application) ---
        // IMPORTANT: Check conflicts against the ORIGINAL state (ctx.State), not next.EffectInstances.
        // EffectInstance.Clone() uses a shallow copy of the EffectInstances list, so existing
        // EffectInstance objects are SHARED between original and cloned state. Modifying the
        // cloned effect (adding new positions) also modifies the original, causing column
        // conflicts to be incorrectly detected. We check against ctx.State which is unmodified.
        var (appliedColumns, appliedPositions) = RiverGeometry.ResolveNonConflictingPaths(
            paths, ctx.State.EffectInstances);

        // --- Zero-position rule: if all paths conflict, activation fails atomically ---
        if (appliedColumns.Length == 0)
            return CommandSkillResult.Failure(ctx.State, "ALL_PATHS_BLOCKED",
                "All selected river paths are blocked by existing terrain Effects. Activation aborted.");

        // --- Build state changes ---
        var next = ctx.State.Clone();
        var creationOrder = next.NextCreationOrder++;
        var effectId = Guid.NewGuid();

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = "terrain",         // U7 = A: terrain Effects are stealable
            ["restriction"] = "river_crossing_block",
            ["paths"] = appliedColumns,
            ["originalPaths"] = paths,      // Original requested paths for replay reference
            ["appliedPaths"] = appliedColumns
        };

        var effect = Effects.EffectFactory.Create(
            effectId: effectId,
            code: SkillKeys.BinhLamThuyHien,
            skillId: ctx.SkillId,
            creator: ctx.ActorSide,
            creationOrder: creationOrder,
            duration: 1, // 1 user player turn
            targetPositions: appliedPositions,
            payload: payload);
        next.EffectInstances.Add(effect);

        var events = new object[]
        {
            new
            {
                type = "effect.created",
                effectId = effectId,
                code = SkillKeys.BinhLamThuyHien,
                creator = ctx.ActorSide.ToString(),
                creationOrder = creationOrder,
                targetPositions = appliedPositions.Select(p => new { p.X, p.Y }).ToArray(),
                partialApplication = appliedColumns.Length < 3,
                blockedColumns = paths.Except(appliedColumns).ToArray()
            }
        };

        return CommandSkillResult.Success(next, events);
    }
}

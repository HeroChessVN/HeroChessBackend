// Phase 2.4.1: Vạn Cọc Trấn Giang Command Skill handler.
using HeroChess.Rules.Effects;

namespace HeroChess.Rules.Skills;

/// <summary>
/// Handler for Vạn Cọc Trấn Giang.
///
/// Activation places:
/// - A blocking Effect covering 3 consecutive river-crossing paths (columns 4,5,6 per U-GEO = A).
/// - Physical stakes as Cannon-screen obstacles at those positions.
///
/// Separates two concepts:
/// A. Blocking Effect: prevents opponent river crossing through affected columns.
/// B. Physical stakes: cannon screen obstacles with independent 2-turn lifetime.
///
/// Physical stake lifetime:
/// - Measured in the original placer's turns (not the blocking Effect's duration).
/// - Independent from the blocking Effect's state.
/// - Does NOT reset when the blocking Effect is countered or transferred.
/// - Decremented by TurnLifecycle at each original placer turn.
///
/// Blocking Effect duration: 2 Creator player turns (U-DUR = A).
/// Physical stake lifetime: 2 original placer turns.
/// Cooldown: 2 player turns.
///
/// Validation:
/// 1. target.paths must be exactly 3 consecutive valid river columns per U-GEO = A.
/// 2. All 3 paths must be free of conflict with existing river-blocking Effects.
///    (If any path conflicts, the activation fails atomically — no partial application.)
/// </summary>
public sealed class VanCocTranGiangHandler : ICommandSkillHandler
{
    public string ImplementationKey => SkillKeys.VanCocTranGiang;

    public CommandSkillResult Execute(CommandSkillContext ctx)
    {
        // --- Parse target ---
        var paths = RiverGeometry.TryParsePaths(ctx.Target);
        if (paths == null || paths.Length != 3)
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET",
                "Vạn Cọc requires a target with exactly 3 river path column indices.");

        // --- Validate 3 consecutive river columns per U-GEO = A ---
        Array.Sort(paths);
        if (!RiverGeometry.IsValidRiverPathSelection(paths))
            return CommandSkillResult.Failure(ctx.State, "INVALID_RIVER_PATH",
                "Vạn Cọc requires exactly 3 consecutive river columns within {4, 5, 6}.");

        // --- Check for conflicts with existing river-blocking Effects ---
        // All 3 paths must be free; if any conflicts, activation fails atomically.
        foreach (var col in paths)
        {
            if (RiverGeometry.IsColumnConflicting(col, ctx.State.EffectInstances))
                return CommandSkillResult.Failure(ctx.State, "PATH_OCCUPIED",
                    $"Column {col} is already occupied by an existing river-blocking Effect.");
        }

        // --- Build state changes ---
        var next = ctx.State.Clone();
        var creationOrder = next.NextCreationOrder++;
        var effectId = Guid.NewGuid();

        // A. Create the blocking Effect
        var positions = RiverGeometry.ResolvePositions(paths);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = "river_blocking",
            ["selectedPaths"] = paths,
            ["placer"] = ctx.ActorSide.ToString()
        };
        var effect = Effects.EffectFactory.Create(
            effectId: effectId,
            code: SkillKeys.VanCocTranGiang,
            skillId: ctx.SkillId,
            creator: ctx.ActorSide,
            creationOrder: creationOrder,
            duration: 2, // 2 Creator player turns
            targetPositions: positions,
            payload: payload);
        next.EffectInstances.Add(effect);

        // B. Create physical stake obstacles (one per column, all rows)
        // Each stake has its own lifetime tracked separately from the blocking Effect.
        // Lifetime = 2 turns of the original placer (confirmed gameplay).
        foreach (var col in paths)
        {
            for (var row = 0; row < 10; row++)
            {
                var obstacleId = Guid.NewGuid();
                var pos = new BoardPoint(col, row);
                next.Obstacles.Add(new ObstacleState(obstacleId, pos, "stake") { RemainingLifetime = 2 });
                next.StakeMetadata[obstacleId] = new StakeMetadata
                {
                    Placer = ctx.ActorSide,
                    EffectId = effectId
                };
            }
        }

        // --- Emit events ---
        var events = new object[]
        {
            new
            {
                type = "effect.created",
                effectId = effectId,
                code = SkillKeys.VanCocTranGiang,
                creator = ctx.ActorSide.ToString(),
                creationOrder = creationOrder,
                targetPositions = positions.Select(p => new { p.X, p.Y }).ToArray()
            },
            new
            {
                type = "obstacle.created",
                obstacleCount = paths.Length * 10,
                kind = "stake",
                creator = ctx.ActorSide.ToString(),
                effectId = effectId
            }
        };

        return CommandSkillResult.Success(next, events);
    }
}

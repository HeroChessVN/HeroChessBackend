namespace HeroChess.Rules;
using HeroChess.Rules.Effects;
using HeroChess.Rules.Skills;

/// <summary>
/// Turn-start lifecycle processing.
///
/// Runs exactly once per authoritative Player Turn (enforced by the caller).
/// Applies:
/// 1. Cooldown decrement for all Skills owned by the current player.
/// 2. Effect duration decrement for all Effects whose Creator == current player (U-DUR = A).
/// 3. Effect expiration when RemainingDuration reaches 0.
/// 4. Physical stake obstacle lifetime decrement (per original placer's turns).
/// 5. Physical stake removal when RemainingLifetime reaches 0.
/// 6. Pending Phản Kỳ steals finalized: PendingController becomes official controller.
/// 7. Effect removal by original Creator (steal cancelled).
///
/// All mutations operate on the cloned state. The caller is responsible for
/// committing the result atomically.
///
/// Thread-safety: caller holds the match lock.
/// </summary>
public static class TurnLifecycle
{
    /// <summary>
    /// Result of applying the turn lifecycle to a state.
    /// </summary>
    public sealed record LifecycleResult(
        GameState State,
        IReadOnlyList<EffectExpiredEvent> ExpiredEffects,
        IReadOnlyList<ObstacleRemovedEvent> RemovedStakes,
        IReadOnlyList<CooldownDecrementedEvent> DecrementedCooldowns,
        IReadOnlyList<PendingStealFinalizedEvent> FinalizedSteals,
        IReadOnlyList<EffectRemovedEvent> RemovedEffects);

    /// <summary>
    /// Internal result of core lifecycle (Steps 1-4), without Creator Cancellation.
    /// </summary>
    private sealed record CoreLifecycleResult(
        GameState State,
        IReadOnlyList<EffectExpiredEvent> ExpiredEffects,
        IReadOnlyList<ObstacleRemovedEvent> RemovedStakes,
        IReadOnlyList<CooldownDecrementedEvent> DecrementedCooldowns,
        IReadOnlyList<PendingStealFinalizedEvent> FinalizedSteals);

    /// <summary>
    /// Emitted when an Effect expires (RemainingDuration reached 0).
    /// </summary>
    public sealed record EffectExpiredEvent(Guid EffectId, Guid SkillId, Side Creator, string Code);

    /// <summary>
    /// Emitted when a physical stake obstacle is removed (RemainingLifetime reached 0).
    /// </summary>
    public sealed record ObstacleRemovedEvent(Guid ObstacleId, BoardPoint Position, string Kind, Guid? EffectId);

    /// <summary>
    /// Emitted when a Skill's cooldown is decremented from > 0 to > 0 (not on expiry).
    /// </summary>
    public sealed record CooldownDecrementedEvent(Side Side, int SlotNo, Guid SkillId, int NewCooldown);

    /// <summary>
    /// Emitted when a pending Phản Kỳ steal is finalized.
    /// </summary>
    public sealed record PendingStealFinalizedEvent(Guid EffectId, Side NewController);

    /// <summary>
    /// Emitted when a stolen effect is removed by its Creator during their following turn.
    /// </summary>
    public sealed record EffectRemovedEvent(Guid EffectId, Side RemovedBy);

    /// <summary>
    /// Applies the turn-start lifecycle for the given side to a cloned state.
    ///
    /// This method performs the COMPLETE lifecycle exactly once:
    /// - Steps 1-4: Core lifecycle (cooldowns, effects, stakes, pending finalization)
    /// - Step 5: Creator Cancellation (if resolveCreatorCancellation is true)
    ///
    /// Exactly-once semantics are enforced by the caller (checking ProcessedTurns before calling).
    ///
    /// Timing (confirmed gameplay — §13.1):
    /// - Cooldown is measured in player turns.
    /// - Effect duration is measured in the Creator's player turns (U-DUR = A).
    /// - Turn-start expiration is processed BEFORE the player can act.
    /// - If RemainingDuration reaches 0 → Effect ends immediately.
    /// - Disabled Effects continue their timers (Disabled ≠ Ended §9.2).
    /// - Physical stake lifetime is measured in the original placer's turns.
    ///
    /// Phản Kỳ pending steal timing:
    /// - When a steal is initiated, PendingController is set.
    /// - At the start of the Creator's turn, PendingController is finalized.
    /// - The original Creator can cancel the stolen effect during their turn.
    ///
    /// Creator cancellation (internal Phản Kỳ resolution, NOT a Command Skill):
    /// - During the Creator's turn, they can cancel stolen effects.
    /// - Cancellation is an INTERNAL RESOLUTION, NOT a separate skill.
    /// - Cancellation: sets effect State = Ended; does NOT create a new Effect.
    /// - Cancellation: does NOT change Creator, EffectId, CreationOrder, or Duration.
    /// - Cancellation: does NOT trigger a new steal or cooldown.
    /// - Cancellation: recorded as "phan_ky.cancelled" event.
    ///
    /// Hero Skill — Quang Trung cooldown:
    /// - When a Quang Trung hero skill's cooldown reaches 0, set cooldownReady = 1 on
    ///   the piece's TraitState so the special movement becomes available this turn.
    /// </summary>
    /// <param name="state">The current game state (will be cloned internally).</param>
    /// <param name="sideToMove">The side whose turn is starting.</param>
    /// <param name="resolveCreatorCancellation">When true, processes stolen effect cancellation during Creator's turn.</param>
    /// <returns>A LifecycleResult containing the updated state and emitted events.</returns>
    public static LifecycleResult Apply(GameState state, Side sideToMove, bool resolveCreatorCancellation = false)
    {
        // Step 1-4: Apply core lifecycle exactly once (returns cloned state with mutations)
        var core = ApplyCore(state, sideToMove);
        var removedEffects = new List<EffectRemovedEvent>();

        // Step 5: Creator Cancellation (only runs if resolveCreatorCancellation is true)
        if (resolveCreatorCancellation)
        {
            ApplyCreatorCancellation(core.State, sideToMove, removedEffects);
        }

        return new LifecycleResult(
            core.State,
            core.ExpiredEffects,
            core.RemovedStakes,
            core.DecrementedCooldowns,
            core.FinalizedSteals,
            removedEffects);
    }

    /// <summary>
    /// Applies core lifecycle (Steps 1-4) exactly once:
    /// 1. Cooldown decrement (and hero-skill sync for Quang Trung)
    /// 2. Effect duration decrement
    /// 3. Physical stake lifetime decrement
    /// 4. Pending Phản Kỳ finalization
    /// </summary>
    private static CoreLifecycleResult ApplyCore(GameState state, Side sideToMove)
    {
        var next = state.Clone();
        var expiredEffects = new List<EffectExpiredEvent>();
        var removedStakes = new List<ObstacleRemovedEvent>();
        var decrementedCooldowns = new List<CooldownDecrementedEvent>();
        var finalizedSteals = new List<PendingStealFinalizedEvent>();

        // Step 1: Decrement cooldowns for all Skills owned by the current player.
        // Cooldown is measured in player turns (§13.1). Decrementing happens at the
        // start of the owner's next player turn.
        if (next.SkillStates.TryGetValue(sideToMove, out var skillList))
        {
            for (var i = 0; i < skillList.Count; i++)
            {
                var skill = skillList[i];
                if (skill.CooldownRemaining > 0)
                {
                    var newCooldown = skill.CooldownRemaining - 1;
                    // Replace with a new SkillState (record is immutable after construction).
                    skillList[i] = skill with { CooldownRemaining = newCooldown };
                    // Hero Skill — Quang Trung: when cooldown expires (1 → 0), make special movement available.
                    if (newCooldown == 0)
                        SetQuangTrungCooldownReady(next, sideToMove);
                    // Only emit event if the cooldown didn't expire (newCooldown > 0).
                    // If it expired, the next turn it will simply be 0 and won't decrement further.
                    if (newCooldown > 0)
                    {
                        decrementedCooldowns.Add(new CooldownDecrementedEvent(
                            sideToMove, skill.SlotNo, skill.SkillId, newCooldown));
                    }
                }
            }
        }

        // Step 2: Decrement Effect RemainingDuration for all Effects whose Creator == sideToMove.
        // U-DUR = A: TimerOwner = Creator. Only the Creator's turn decrements the timer.
        // RemainingDuration is never reset by control transfer or disabling.
        // Disabled Effects continue their timers — Disabled ≠ Ended (§9.2).
        foreach (var effect in next.EffectInstances)
        {
            // Decrement for all non-ended Effects whose Creator == sideToMove.
            // Active and Disabled effects both count down.
            if (effect.State != EffectStateValue.Ended && effect.Creator == sideToMove)
            {
                effect.RemainingDuration -= 1;
                if (effect.RemainingDuration == 0)
                {
                    effect.State = EffectStateValue.Ended;
                    expiredEffects.Add(new EffectExpiredEvent(
                        effect.EffectId, effect.SkillId, effect.Creator, effect.Code));
                }
            }
        }

        // Step 3: Decrement obstacle lifetime for all obstacles placed by sideToMove.
        // Applies to: Vạn Cọc stakes ("stake"), Thành, Rào, THD Tượng Cọc.
        // Lifetime is measured in the original placer's turns.
        var j = 0;
        while (j < next.Obstacles.Count)
        {
            var obstacle = next.Obstacles[j];
            if (obstacle.RemainingLifetime is null or <= 0)
            {
                j++;
                continue;
            }

            // Determine the placer of this obstacle.
            var placer = GetObstaclePlacer(next, obstacle);
            if (placer == sideToMove)
            {
                var newLifetime = obstacle.RemainingLifetime!.Value - 1;
                next.Obstacles[j] = obstacle with { RemainingLifetime = newLifetime };
                if (newLifetime == 0)
                {
                    var removed = next.Obstacles[j];
                    removedStakes.Add(new ObstacleRemovedEvent(
                        removed.ObstacleId, removed.Position, removed.Kind,
                        GetStakeEffectId(next, removed)));
                    next.Obstacles.RemoveAt(j);
                    // Don't increment j — the next element slides into current position.
                    continue;
                }
            }
            j++;
        }

        // Step 4: Process pending Phản Kỳ steals.
        //
        // CONFIRMED TIMING:
        // - B uses Phản Kỳ during B's turn → PendingController is set
        // - B becomes official Controller AFTER B's current turn ends
        // - During A's (Creator's) turn, A can cancel the steal
        //
        // Implementation: When Apply runs for the Creator (sideToMove == Creator),
        // check if there are any effects with PendingController != null (stolen by the other side).
        // If so, finalize the transfer: update PositionControllers to PendingController.
        foreach (var effect in next.EffectInstances)
        {
            if (effect.PendingController.HasValue && effect.State == EffectStateValue.Active)
            {
                // Finalize the steal: transfer all positions to the pending controller
                foreach (var pos in effect.TargetPositions)
                {
                    effect.PositionControllers[pos] = effect.PendingController.Value;
                }
                finalizedSteals.Add(new PendingStealFinalizedEvent(effect.EffectId, effect.PendingController.Value));
                effect.PendingController = null;
            }
        }

        return new CoreLifecycleResult(next, expiredEffects, removedStakes, decrementedCooldowns, finalizedSteals);
    }

    /// <summary>
    /// Step 5: Process stolen effect cancellation by the original Creator.
    ///
    /// INTERNAL PHẢN KỲ RESOLUTION (NOT a Command Skill):
    /// - During the Creator's turn, they can cancel stolen effects.
    /// - Cancellation: sets effect State = Ended; does NOT create a new Effect.
    /// - Cancellation: does NOT change Creator, EffectId, CreationOrder, or Duration.
    /// - Cancellation: emits "phan_ky.cancelled" event.
    /// </summary>
    private static void ApplyCreatorCancellation(GameState state, Side sideToMove, List<EffectRemovedEvent> removedEffects)
    {
        foreach (var effect in state.EffectInstances)
        {
            if (effect.Creator == sideToMove && effect.State == EffectStateValue.Active)
            {
                // Check if any position is controlled by the opponent (stolen)
                var isStolen = effect.PositionControllers.Values.Any(c => c != sideToMove);
                if (isStolen)
                {
                    // Cancel the stolen effect
                    effect.State = EffectStateValue.Ended;
                    removedEffects.Add(new EffectRemovedEvent(effect.EffectId, sideToMove));
                }
            }
        }
    }

    /// <summary>
    /// Checks whether a given turn has already been processed for a given side.
    /// Used by the caller to enforce exactly-once semantics.
    /// </summary>
    public static bool IsTurnProcessed(GameState state, Side side, int turnIndex)
    {
        if (!state.ProcessedTurns.TryGetValue(side, out var processed))
            return false;
        return processed.Contains(turnIndex);
    }

    /// <summary>
    /// Marks a turn as processed for a given side.
    /// Call this AFTER successfully committing the lifecycle result.
    /// </summary>
    public static void MarkTurnProcessed(GameState state, Side side, int turnIndex)
    {
        if (!state.ProcessedTurns.ContainsKey(side))
            state.ProcessedTurns[side] = new List<int>();
        if (!state.ProcessedTurns[side].Contains(turnIndex))
            state.ProcessedTurns[side].Add(turnIndex);
    }

    // Helper: retrieves the original placer side for an obstacle.
    // Checks ObstacleMetadata first (Thành, Rào, THD Cọc), then StakeMetadata (Vạn Cọc).
    private static Side GetObstaclePlacer(GameState state, ObstacleState obstacle)
    {
        if (state.ObstacleMetadata.TryGetValue(obstacle.ObstacleId, out var meta))
            return meta.Placer;
        if (state.StakeMetadata.TryGetValue(obstacle.ObstacleId, out var stakeMeta))
            return stakeMeta.Placer;
        return sideToMove_Unknown;
    }

    private static Guid? GetStakeEffectId(GameState state, ObstacleState stake) =>
        state.StakeMetadata.TryGetValue(stake.ObstacleId, out var meta) ? meta.EffectId : null;

    // Marker for unknown placer — should not occur for valid stake obstacles.
    private static readonly Side sideToMove_Unknown = (Side)255;

    // ============================================================
    // Hero Skill integration
    // ============================================================

    /// <summary>
    /// Hero Skill — Quang Trung cooldown-ready sync.
    ///
    /// When Quang Trung's team skill cooldown expires (1 → 0), the special movement
    /// becomes available. This is done by setting cooldownReady = 1 on the Quang Trung
    /// piece's TraitState.
    ///
    /// The piece is identified by:
    /// - MovementImplementationKey == "general.orthogonal_range_3" AND
    /// - Either TraitImplementationKey contains "quang_trung" or "quang-trung" OR
    ///   TraitKind == "special_move" (fallback — Quang Trung is the only General with
    ///   special_move on the current roster).
    /// </summary>
    private static void SetQuangTrungCooldownReady(GameState state, Side side)
    {
        foreach (var piece in state.Pieces)
        {
            if (piece.Side != side) continue;
            if (piece.MovementImplementationKey != "general.orthogonal_range_3") continue;
            if (piece.TraitImplementationKey is not null &&
                (piece.TraitImplementationKey.Contains("quang_trung", StringComparison.OrdinalIgnoreCase) ||
                 piece.TraitImplementationKey.Contains("quang-trung", StringComparison.OrdinalIgnoreCase)))
            {
                piece.TraitState[SkillKeys.QuangTrungCooldownKey] = 1;
                return; // Exactly one Quang Trung piece per side.
            }
            if (piece.TraitKind == "special_move")
            {
                // Fallback: any General with special_move is treated as Quang Trung.
                // (No other General on the current roster has special_move.)
                piece.TraitState[SkillKeys.QuangTrungCooldownKey] = 1;
                return;
            }
        }
    }
}

/// <summary>
/// Metadata for physical stake obstacles, keyed by ObstacleId.
/// Tracks the original placer (immutable) and the linked EffectId.
/// Stored on GameState so it is persisted and cloned correctly.
///
/// Architecture note (A1 from the implementation plan):
/// This is where we store the physical stake lifetime origin information.
/// The RemainingLifetime is on ObstacleState and decrements per placer turn.
/// </summary>
public sealed class StakeMetadata
{
    /// <summary>
    /// The Side that originally placed this stake. Immutable — never changes.
    /// </summary>
    public Side Placer { get; init; }

    /// <summary>
    /// The EffectId of the blocking Effect this stake is linked to.
    /// May be null if the Effect has already expired.
    /// </summary>
    public Guid? EffectId { get; init; }
}

/// <summary>
/// Step 6: General obstacle metadata.
/// Tracks the original placer Side for any obstacle (Thành, Rào, THD Cọc).
/// Used by TurnLifecycle to determine whose turn decrements the obstacle's lifetime.
/// </summary>
public sealed class ObstacleMetadata
{
    /// <summary>
    /// The Side that originally placed this obstacle. Immutable — never changes.
    /// </summary>
    public Side Placer { get; init; }
}

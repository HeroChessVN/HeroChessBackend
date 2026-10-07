namespace HeroChess.Rules.Effects;

/// <summary>
/// Factory for creating EffectInstance objects with enforced invariants.
/// </summary>
public static class EffectFactory
{
    /// <summary>
    /// Creates a new EffectInstance with required fields initialized.
    ///
    /// Invariants enforced:
    /// - RemainingDuration is initialized to duration (never left uninitialized).
    /// - State is set to Active.
    /// - Every position in targetPositions has an entry in PositionControllers.
    /// - EffectId, Creator, CreationOrder, Duration are immutable after creation.
    /// </summary>
    /// <param name="effectId">Unique identifier for this effect.</param>
    /// <param name="code">Skill code, e.g. "van_coc_tran_giang".</param>
    /// <param name="skillId">The skill that created this effect.</param>
    /// <param name="creator">The Side that created this effect. Immutable.</param>
    /// <param name="creationOrder">Monotonic counter from GameState.NextCreationOrder.</param>
    /// <param name="duration">Total duration in player turns. Immutable.</param>
    /// <param name="targetPositions">All positions covered by this effect.</param>
    /// <param name="initialControllers">Initial controller for each position. Typically the creator.</param>
    /// <param name="payload">Optional skill-dependent payload.</param>
    /// <returns>A new EffectInstance with invariants enforced.</returns>
    public static EffectInstance Create(
        Guid effectId,
        string code,
        Guid skillId,
        Side creator,
        int creationOrder,
        int duration,
        IReadOnlyList<BoardPoint> targetPositions,
        IDictionary<BoardPoint, Side>? initialControllers = null,
        IReadOnlyDictionary<string, object?>? payload = null)
    {
        // Build PositionControllers: every position in targetPositions must have an entry.
        var positionControllers = new Dictionary<BoardPoint, Side>(targetPositions.Count);
        foreach (var pos in targetPositions)
        {
            if (initialControllers != null && initialControllers.TryGetValue(pos, out var ctrl))
                positionControllers[pos] = ctrl;
            else
                positionControllers[pos] = creator;
        }

        // RemainingDuration is initialized to Duration (U-DUR = A).
        // It counts down but is never reset by transfer or disabling.
        return new EffectInstance
        {
            EffectId = effectId,
            Code = code,
            SkillId = skillId,
            Creator = creator,
            CreationOrder = creationOrder,
            Duration = duration,
            RemainingDuration = duration, // initialized to Duration per U-DUR = A
            State = EffectStateValue.Active,
            TargetPositions = targetPositions,
            PositionControllers = positionControllers,
            Payload = payload ?? new Dictionary<string, object?>(StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// Creates a deep clone of an EffectInstance, preserving all field values.
    /// </summary>
    public static EffectInstance Clone(EffectInstance source) => source.Clone();
}

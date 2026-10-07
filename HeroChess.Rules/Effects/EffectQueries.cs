using System.Diagnostics.CodeAnalysis;

namespace HeroChess.Rules.Effects;

/// <summary>
/// Extension methods and query helpers for EffectInstances within GameState.
/// </summary>
public static class EffectQueries
{
    /// <summary>
    /// Returns all active effects (State == Active).
    /// </summary>
    public static IEnumerable<EffectInstance> ActiveEffects(this GameState state) =>
        state.EffectInstances.Where(e => e.State == EffectStateValue.Active);

    /// <summary>
    /// Returns all non-ended effects (State != Ended).
    /// </summary>
    public static IEnumerable<EffectInstance> NonEndedEffects(this GameState state) =>
        state.EffectInstances.Where(e => e.State != EffectStateValue.Ended);

    /// <summary>
    /// Finds an Effect by its EffectId.
    /// </summary>
    public static EffectInstance? FindEffect(this GameState state, Guid effectId) =>
        state.EffectInstances.FirstOrDefault(e => e.EffectId == effectId);

    /// <summary>
    /// Finds an Effect by its EffectId, returns false if not found.
    /// </summary>
    public static bool TryGetEffect(this GameState state, Guid effectId, [NotNullWhen(true)] out EffectInstance? effect)
    {
        effect = state.EffectInstances.FirstOrDefault(e => e.EffectId == effectId);
        return effect != null;
    }

    /// <summary>
    /// Returns all effects created by a specific side.
    /// </summary>
    public static IEnumerable<EffectInstance> EffectsByCreator(this GameState state, Side creator) =>
        state.EffectInstances.Where(e => e.Creator == creator);

    /// <summary>
    /// Returns all active effects created by a specific side.
    /// </summary>
    public static IEnumerable<EffectInstance> ActiveEffectsByCreator(this GameState state, Side creator) =>
        state.EffectInstances.Where(e => e.Creator == creator && e.State == EffectStateValue.Active);

    /// <summary>
    /// Returns all effects that cover a given board position.
    /// </summary>
    public static IEnumerable<EffectInstance> EffectsAtPosition(this GameState state, BoardPoint position) =>
        state.EffectInstances.Where(e => e.TargetPositions.Contains(position));

    /// <summary>
    /// Returns all active effects that cover a given board position.
    /// </summary>
    public static IEnumerable<EffectInstance> ActiveEffectsAtPosition(this GameState state, BoardPoint position) =>
        state.EffectInstances.Where(e => e.State == EffectStateValue.Active && e.TargetPositions.Contains(position));

    /// <summary>
    /// Returns all effects with a specific skill code.
    /// </summary>
    public static IEnumerable<EffectInstance> EffectsByCode(this GameState state, string code) =>
        state.EffectInstances.Where(e => string.Equals(e.Code, code, StringComparison.Ordinal));

    /// <summary>
    /// Returns all active effects with a specific skill code.
    /// </summary>
    public static IEnumerable<EffectInstance> ActiveEffectsByCode(this GameState state, string code) =>
        state.EffectInstances.Where(e => e.State == EffectStateValue.Active &&
                                          string.Equals(e.Code, code, StringComparison.Ordinal));
}

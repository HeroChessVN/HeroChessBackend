namespace HeroChess.Rules.Effects;

/// <summary>
/// The state of an Effect in the game.
/// </summary>
public enum EffectStateValue
{
    /// <summary>
    /// The Effect is active and its behavior applies.
    /// </summary>
    Active,

    /// <summary>
    /// The Effect exists but its behavior is suspended. Duration continues to count down.
    /// </summary>
    Disabled,

    /// <summary>
    /// The Effect has ended. It will be pruned on the next state cleanup.
    /// </summary>
    Ended
}

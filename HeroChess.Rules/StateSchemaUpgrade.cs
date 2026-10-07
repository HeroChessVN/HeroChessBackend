namespace HeroChess.Rules;

/// <summary>
/// Handles schema upgrades from older GameState versions to the current version.
///
/// The current schema version is 4 (v4).
/// v3 states are migrated to v4 by initializing new Effect-related fields.
/// </summary>
public static class StateSchemaUpgrade
{
    /// <summary>
    /// The current schema version.
    /// </summary>
    public const int CurrentVersion = 4;

    /// <summary>
    /// Upgrades a GameState to the current schema version if needed.
    /// Returns the same object if already at current version; otherwise returns
    /// a new GameState with the upgrade applied.
    ///
    /// Upgrade from v3 → v4:
    /// - Initializes EffectInstances to an empty list.
    /// - Initializes NextCreationOrder to 0.
    /// - Initializes ProcessedTurns to a new dictionary with empty entries for both sides.
    /// </summary>
    public static GameState UpgradeToCurrent(GameState state)
    {
        if (state.StateSchemaVersion >= CurrentVersion)
            return state;

        if (state.StateSchemaVersion == 3)
            return UpgradeFromV3(state);

        // For any version older than v3, upgrade through v3 first.
        var v3State = UpgradeFromOlder(state);
        return UpgradeFromV3(v3State);
    }

    /// <summary>
    /// Checks whether the given state is at or above the current schema version.
    /// </summary>
    public static bool IsCurrentSchema(GameState state) =>
        state.StateSchemaVersion >= CurrentVersion;

    private static GameState UpgradeFromV3(GameState state)
    {
        // v3 → v4: Add EffectInstances, NextCreationOrder, ProcessedTurns.
        // These fields are initialized to safe empty/default values.
        // The EffectInstances list starts empty (no pre-existing effects to migrate).
        // ProcessedTurns starts empty — TurnLifecycle will populate it as turns are processed.
        var upgraded = new GameState
        {
            StateSchemaVersion = 4,
            EffectInstances = new List<Effects.EffectInstance>(),
            NextCreationOrder = 0,
            ProcessedTurns = new Dictionary<Side, List<int>>()
            {
                [Side.Red] = new List<int>(),
                [Side.Black] = new List<int>()
            },
            StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
            RulesetCode = state.RulesetCode,
            ContentVersion = state.ContentVersion,
            SideToMove = state.SideToMove,
            Version = state.Version,
            TurnIndex = state.TurnIndex,
            CountedActions = state.CountedActions,
            Result = state.Result,
            EndReason = state.EndReason,
            Pieces = state.Pieces,
            Obstacles = state.Obstacles,
            PendingEffects = state.PendingEffects,
            ConsecutiveTimeouts = state.ConsecutiveTimeouts,
            SkillStates = state.SkillStates
        };
        return upgraded;
    }

    // Upgrade from any version older than v3 by first upgrading to v3 defaults.
    // Since v3 is the "last version before effects", older versions get the same treatment.
    private static GameState UpgradeFromOlder(GameState state)
    {
        // We don't have detailed migration paths for pre-v3 versions.
        // Apply v3 defaults as the baseline.
        var upgraded = new GameState
        {
            StateSchemaVersion = 3,
            EffectInstances = new List<Effects.EffectInstance>(),
            NextCreationOrder = 0,
            ProcessedTurns = new Dictionary<Side, List<int>>()
            {
                [Side.Red] = new List<int>(),
                [Side.Black] = new List<int>()
            },
            StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
            RulesetCode = state.RulesetCode,
            ContentVersion = state.ContentVersion,
            SideToMove = state.SideToMove,
            Version = state.Version,
            TurnIndex = state.TurnIndex,
            CountedActions = state.CountedActions,
            Result = state.Result,
            EndReason = state.EndReason,
            Pieces = state.Pieces,
            Obstacles = state.Obstacles,
            PendingEffects = state.PendingEffects,
            ConsecutiveTimeouts = state.ConsecutiveTimeouts,
            SkillStates = state.SkillStates
        };
        return upgraded;
    }
}

namespace HeroChess.Rules.Effects;

/// <summary>
/// Represents one active Effect in the game.
///
/// Invariants (enforced by EffectFactory):
/// - EffectId, Creator, CreationOrder are set once at creation and never mutated.
/// - Duration is set once at creation and never mutated.
/// - RemainingDuration is initialized to Duration and counts down; it is never reset
///   by control transfer or disabling.
/// - Every position in TargetPositions has an entry in PositionControllers.
/// - State is Active | Disabled | Ended.
/// </summary>
public sealed class EffectInstance
{
    /// <summary>
    /// Unique identifier for this Effect within a match. Assigned once; never changes.
    /// </summary>
    public Guid EffectId { get; init; }

    /// <summary>
    /// Stable kind identifier, e.g. "van_coc_tran_giang", "binh_lam_thuy_hien".
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// The Skill that produced this Effect.
    /// </summary>
    public Guid SkillId { get; init; }

    /// <summary>
    /// The Side that created this Effect. Immutable — NEVER changes after creation.
    /// </summary>
    public Side Creator { get; init; }

    /// <summary>
    /// Maps each affected board position to the Side currently controlling that portion.
    /// Key = position, Value = Side currently controlling this portion.
    /// Every position in TargetPositions has an entry here.
    /// Mutable — updated by control transfer operations.
    /// </summary>
    public Dictionary<BoardPoint, Side> PositionControllers { get; set; } = new();

    /// <summary>
    /// When a Phản Kỳ Đoạt Thế steal is initiated but not yet finalized.
    /// The official controller remains the original until the steal is finalized.
    /// Null = no pending steal.
    /// </summary>
    public Side? PendingController { get; set; }

    /// <summary>
    /// Monotonic counter assigned at creation. Immutable — never changes.
    /// </summary>
    public int CreationOrder { get; init; }

    /// <summary>
    /// Current state: Active | Disabled | Ended.
    /// </summary>
    public EffectStateValue State { get; set; }

    /// <summary>
    /// The total duration of this Effect, measured in player turns.
    /// Set at creation per Skill rules. Immutable — never changes.
    /// </summary>
    public int Duration { get; init; }

    /// <summary>
    /// Remaining shared duration of this Effect.
    /// Initialized to Duration at creation.
    /// Counts down at the start of each subsequent shared board turn.
    /// RemainingDuration never resets on transfer or disabling.
    /// </summary>
    public int RemainingDuration { get; set; }

    /// <summary>
    /// All board positions this Effect covers.
    /// U-GEO = A: populated via column-based IRiverPathDefinition.
    /// </summary>
    public IReadOnlyList<BoardPoint> TargetPositions { get; init; } = Array.Empty<BoardPoint>();

    /// <summary>
    /// Skill-dependent payload. Key = string, value = object? for flexibility.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Payload { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Creates a deep clone of this EffectInstance.
    /// All fields are copied; collections are cloned to avoid shared references.
    /// </summary>
    public EffectInstance Clone()
    {
        var copy = new EffectInstance
        {
            EffectId = EffectId,
            Code = Code,
            SkillId = SkillId,
            Creator = Creator,
            CreationOrder = CreationOrder,
            State = State,
            Duration = Duration,
            RemainingDuration = RemainingDuration,
            TargetPositions = new List<BoardPoint>(TargetPositions),
            Payload = new Dictionary<string, object?>(Payload, StringComparer.Ordinal),
            PendingController = PendingController
        };
        foreach (var kvp in PositionControllers)
            copy.PositionControllers[kvp.Key] = kvp.Value;
        return copy;
    }
}

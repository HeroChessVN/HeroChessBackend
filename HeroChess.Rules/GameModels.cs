// Vai trò file: Model bàn cờ thuần C#, không DB/HTTP/UnityEngine; state có thể clone và serialize cho replay/undo.
using System.Text.Json;
namespace HeroChess.Rules;

// Side: Hai bên Red và Black.
public enum Side { Red, Black }
// PieceClass: Bảy loại quân cơ bản; khác hero cụ thể.
public enum PieceClass { General, Advisor, Elephant, Rook, Cannon, Horse, Soldier }
// PieceStatus: Trạng thái quân; PendingRevive mới là chỗ dự phòng dữ liệu.
public enum PieceStatus { Alive, Captured, PendingRevive }

// BoardPoint: Tọa độ kiểu giá trị (record struct); bàn có x 0..8, y 0..9.
public readonly record struct BoardPoint(int X, int Y)
{
    public bool IsOnBoard => X is >= 0 and <= 8 && Y is >= 0 and <= 9;
}

// PieceState: Một quân cụ thể trong trận, có PieceId riêng, vị trí và trait state.
public sealed class PieceState
{
    public Guid PieceId { get; init; }
    public Guid HeroId { get; init; }
    public Side Side { get; init; }
    public PieceClass Class { get; init; }
    public int SetupPoints { get; init; }
    public BoardPoint? Position { get; set; }
    public BoardPoint StartPosition { get; init; }
    public PieceStatus Status { get; set; } = PieceStatus.Alive;
    public string? MovementImplementationKey { get; init; }
    public string? TraitKind { get; init; }
    public string? TraitImplementationKey { get; init; }
    public string? HeroName { get; init; }
    public string? TraitName { get; init; }
    public string? TraitDescription { get; init; }
    public JsonElement? TraitParameters { get; init; }
    public Dictionary<string, int?> TraitState { get; set; } = new(StringComparer.Ordinal);
    public List<EffectState> Effects { get; set; } = new();

    // Clone: Sao chép state và các collection mutable; mô phỏng nước đi/undo không được sửa chung object gốc.
    public PieceState Clone()
    {
        var copy = new PieceState
        {
            PieceId = PieceId,
            HeroId = HeroId,
            Side = Side,
            Class = Class,
            SetupPoints = SetupPoints,
            Position = Position,
            StartPosition = StartPosition,
            Status = Status,
            MovementImplementationKey = MovementImplementationKey,
            TraitKind = TraitKind,
            TraitImplementationKey = TraitImplementationKey,
            HeroName = HeroName,
            TraitName = TraitName,
            TraitDescription = TraitDescription,
            TraitParameters = TraitParameters
        };
        foreach (var pair in TraitState) copy.TraitState[pair.Key] = pair.Value;
        copy.Effects.AddRange(Effects.Select(x => x with { }));
        return copy;
    }
}

// EffectState: Dữ liệu hiệu ứng trên quân; chưa đồng nghĩa có engine xử lý hiệu ứng.
public sealed record EffectState(string Code, Guid? SourcePieceId, int? RemainingTurns);
// ObstacleState: Vật cản tại một ô trong state.
public sealed record ObstacleState(Guid ObstacleId, BoardPoint Position, string Kind, int? RemainingLifetime = null);
// PendingEffectState: Dữ liệu hiệu ứng chờ, chưa có cơ chế resolution hoàn chỉnh.
public sealed record PendingEffectState(string Code, Side Owner, int? ExpiresAtTurn);
// SkillState: Charge/cooldown của một skill theo bên; hiện chủ yếu là dữ liệu snapshot.
public sealed record SkillState(int SlotNo, Guid SkillId, int? UsesRemaining, int CooldownRemaining, string ImplementationKey,
    string? Name = null, string? Description = null, JsonElement? Parameters = null);
// LegalMove: Một nước hợp lệ do server sinh, gồm quân bị ăn nếu có.
public sealed record LegalMove(Guid PieceId, BoardPoint From, BoardPoint To, Guid? CapturedPieceId = null);
// MoveAction: Ý định đi một quân tới ô đích.
public sealed record MoveAction(Guid PieceId, BoardPoint To);

// GameState: Bàn cờ runtime dùng cho tính luật/serialize; không phải EF entity.
public sealed class GameState
{
    public int StateSchemaVersion { get; init; } = 4;
    public string RulesetCode { get; init; } = "prototype-v0.1";
    public string ContentVersion { get; init; } = "development";
    public Side SideToMove { get; set; } = Side.Red;
    public int Version { get; set; }
    public int TurnIndex { get; set; }
    public int CountedActions { get; set; }
    public Dictionary<Side, int> ConsecutiveTimeouts { get; set; } = new()
    {
        [Side.Red] = 0,
        [Side.Black] = 0
    };
    public string? Result { get; set; }
    public string? EndReason { get; set; }
    public List<PieceState> Pieces { get; set; } = new();
    public List<ObstacleState> Obstacles { get; set; } = new();
    public List<PendingEffectState> PendingEffects { get; set; } = new();
    public Dictionary<Side, List<SkillState>> SkillStates { get; set; } = new()
    {
        [Side.Red] = new(),
        [Side.Black] = new()
    };

    // --- Phase 2: Effect system (v4 schema) ---
    // All active EffectInstances in the match.
    public List<Effects.EffectInstance> EffectInstances { get; set; } = new();
    // Monotonic counter for EffectInstance.CreationOrder. Incremented each time a new
    // Effect is created. Never reset during a match.
    public int NextCreationOrder { get; set; }
    // Tracks which turns have been processed for each side.
    // Used by TurnLifecycle to implement exactly-once turn-start processing.
    // Key = side, Value = list of TurnIndex values that have been processed for that side.
    public Dictionary<Side, List<int>> ProcessedTurns { get; set; } = new()
    {
        [Side.Red] = new List<int>(),
        [Side.Black] = new List<int>()
    };
    // Metadata for physical stake obstacles (Vạn Cọc). Key = ObstacleId, Value = StakeMetadata.
    // Records the original owner; lifetime now advances on every shared board turn.
    // This is the selected architecture for A1 (stake lifetime storage).
    public Dictionary<Guid, StakeMetadata> StakeMetadata { get; set; } = new();

    // Step 6: General obstacle metadata. Key = ObstacleId, Value = ObstacleMetadata.
    // Stores the placer Side for any obstacle (Thành, Rào, THD Cọc, etc.).
    // Used to identify the placer, including when projecting hidden Cọc by viewer.
    public Dictionary<Guid, ObstacleMetadata> ObstacleMetadata { get; set; } = new();

    // Step 6: THD Tượng Cọc stakes. Key = PieceId of the THD Tượng piece, Value = ObstacleId of the placed Cọc.
    // Used to track which stake belongs to which piece for recall/duration management.
    public Dictionary<Guid, Guid> ThdTuongCocStakes { get; set; } = new();

    // Clone: Sao chép state và các collection mutable; mô phỏng nước đi/undo không được sửa chung object gốc.
    public GameState Clone()
    {
        var copy = new GameState
        {
            StateSchemaVersion = StateSchemaVersion,
            RulesetCode = RulesetCode,
            ContentVersion = ContentVersion,
            SideToMove = SideToMove,
            Version = Version,
            TurnIndex = TurnIndex,
            CountedActions = CountedActions,
            Result = Result,
            EndReason = EndReason,
            NextCreationOrder = NextCreationOrder
        };
        copy.Pieces.AddRange(Pieces.Select(x => x.Clone()));
        copy.Obstacles.AddRange(Obstacles.Select(x => x with { }));
        copy.PendingEffects.AddRange(PendingEffects.Select(x => x with { }));
        foreach (var entry in ConsecutiveTimeouts) copy.ConsecutiveTimeouts[entry.Key] = entry.Value;
        copy.SkillStates[Side.Red].AddRange(SkillStates[Side.Red].Select(x => x with { }));
        copy.SkillStates[Side.Black].AddRange(SkillStates[Side.Black].Select(x => x with { }));
        // v4: Clone EffectInstances (deep clone each effect)
        copy.EffectInstances.AddRange(EffectInstances.Select(x => x.Clone()));
        // v4: Clone ProcessedTurns
        copy.ProcessedTurns[Side.Red].AddRange(ProcessedTurns[Side.Red]);
        copy.ProcessedTurns[Side.Black].AddRange(ProcessedTurns[Side.Black]);
        // v4: Clone StakeMetadata
        foreach (var kvp in StakeMetadata)
            copy.StakeMetadata[kvp.Key] = kvp.Value;
        // Step 6: Clone ObstacleMetadata
        foreach (var kvp in ObstacleMetadata)
            copy.ObstacleMetadata[kvp.Key] = kvp.Value;
        // Step 6: Clone THD Tượng Cọc stakes
        foreach (var kvp in ThdTuongCocStakes)
            copy.ThdTuongCocStakes[kvp.Key] = kvp.Value;
        return copy;
    }
}

// RuleError: Mã và mô tả lỗi luật.
public sealed record RuleError(string Code, string Message);

// ApplyMoveResult: Kết quả áp dụng luật: accepted, state và error.
public sealed class ApplyMoveResult
{
    // ApplyMoveResult: Constructor nội bộ đóng gói thành công/thất bại, state và lỗi.
    private ApplyMoveResult(bool accepted, GameState state, RuleError? error)
    {
        Accepted = accepted;
        State = state;
        Error = error;
    }

    public bool Accepted { get; }
    public GameState State { get; }
    public RuleError? Error { get; }

    // Success: Tạo kết quả áp dụng thành công với state mới.
    public static ApplyMoveResult Success(GameState state) => new(true, state, null);
    // Failure: Trả lỗi luật cùng state gốc, không giả lập thành công.
    public static ApplyMoveResult Failure(GameState state, string code, string message) => new(false, state, new(code, message));
}

// Vai trò file: Các record request/response dùng chung giữa API và client, gần Java record DTO; không có logic truy cập DB.
using System.Text.Json;

namespace HeroChess.Contracts;

// ApiError: Phong bì lỗi gồm code, message, requestId và details.
public sealed record ApiError(string Code, string Message, string? RequestId = null, object? Details = null);

// MeDto: userId/role/status chung; chỉ Player có playerId/isGuest/coinBalance/elo, Admin trả các trường game null.
public sealed record MeDto(Guid UserId, string DisplayName, string Role, string Status, Guid? PlayerId = null, bool? IsGuest = null, string? CoinBalance = null, int? Elo = null);

// CatalogDto: Gói catalog để client xây đội hình.
public sealed record CatalogDto(
    RulesetDto Ruleset,
    IReadOnlyList<ChessClassDto> Classes,
    IReadOnlyList<LineupSlotDto> Slots,
    IReadOnlyList<HeroDto> Heroes,
    IReadOnlyList<TraitDto> Traits,
    IReadOnlyList<FactionDto> Factions,
    IReadOnlyList<TeamSkillDto> TeamSkills);

// RulesetDto: Bộ luật đang bật được công khai cho client.
public sealed record RulesetDto(Guid Id, string Code, string Name, int SetupBudget, int TurnSeconds, int ActionLimit, JsonElement Config);
// ChessClassDto: Định nghĩa class và số quân cần.
public sealed record ChessClassDto(string Code, string NameVi, int BaseSp, int RequiredCount, string BaseMovementCode);
// LineupSlotDto: Slot và tọa độ xuất phát.
public sealed record LineupSlotDto(int SlotNo, string ClassCode, int StartX, int StartY);
// TraitDto: Metadata trait; không phải state hiệu ứng trong trận.
public sealed record TraitDto(Guid Id, string Code, string Name, string Kind, string ImplementationKey, JsonElement Parameters, string? Description);
// FactionDto: Metadata phe.
public sealed record FactionDto(Guid Id, string Code, string Name, string? Description);
// TeamSkillDto: Metadata skill và điều kiện/charge/cooldown thiết kế.
public sealed record TeamSkillDto(Guid Id, string Code, string Name, Guid? FactionId, string ImplementationKey, JsonElement Parameters, JsonElement Eligibility, int? MaxUses, int? CooldownTurns, string? AssetKey);
// HeroDto: Hero catalog kèm quyền sở hữu của actor; FactionIds còn là list vì contract/schema cũ.
public sealed record HeroDto(Guid Id, string Code, string Name, Guid? CharacterId, string ClassCode, int SetupPoints, TraitDto? Trait, IReadOnlyList<Guid> FactionIds, string? AssetKey, bool IsOwned, string CoinPrice);

// SaveLineupRequest: Input lưu lineup; ExpectedRevision dùng khi sửa.
public sealed record SaveLineupRequest(
    string Name,
    Guid RulesetId,
    IReadOnlyList<LineupEntryInput> Entries,
    IReadOnlyList<LineupSkillInput> Skills,
    int? ExpectedRevision = null);

// LineupEntryInput: Chọn hero/cosmetic cho một slot.
public sealed record LineupEntryInput(int SlotNo, Guid HeroId, Guid? CosmeticId);
// LineupSkillInput: Chọn skill cho một slot.
public sealed record LineupSkillInput(int SlotNo, Guid SkillId);
// LineupValidationError: Lỗi validation, có thể gắn một slot cụ thể.
public sealed record LineupValidationError(string Code, string Message, int? SlotNo = null);
// LineupDto: Đội hình đã lưu, revision, SP và kết quả validation.
public sealed record LineupDto(
    Guid Id,
    string Name,
    int Revision,
    Guid RulesetId,
    IReadOnlyList<LineupEntryInput> Entries,
    IReadOnlyList<LineupSkillInput> Skills,
    int TotalSp,
    int Budget,
    bool IsValid,
    IReadOnlyList<LineupValidationError> ValidationErrors);

// CreateMatchmakingTicketRequest: Yêu cầu queue ranked hoặc bot.
public sealed record CreateMatchmakingTicketRequest(string Mode = "ranked");
// MatchmakingTicketDto: Ticket, trạng thái và matchId sau ghép.
public sealed record MatchmakingTicketDto(Guid TicketId, string Mode, string Status, Guid? MatchId, DateTimeOffset CreatedAt);
// SelectLineupRequest: Chọn lineup có revision mong đợi.
public sealed record SelectLineupRequest(Guid LineupId, int ExpectedRevision);
// MatchSelectionSideDto: Thông tin selection được phép xem; lineup ID của đối thủ bị ẩn.
public sealed record MatchSelectionSideDto(string Side, bool Confirmed, IReadOnlyList<Guid> PublicSkillIds, Guid? OwnLineupId = null, int? OwnLineupRevision = null);
// MatchSelectionDto: Selection hai bên và trạng thái trận.
public sealed record MatchSelectionDto(Guid MatchId, string Mode, string Status, IReadOnlyList<MatchSelectionSideDto> Sides);
// WsTicketDto: Vé mở socket một lần cùng hạn dùng.
public sealed record WsTicketDto(string Ticket, DateTimeOffset ExpiresAt);

// MatchStateDto: State JSON kèm version/deadline/serverNow và dấu settlement.
public sealed record MatchStateDto(
    Guid MatchId,
    string Mode,
    string Status,
    int Version,
    string SideToMove,
    int TurnIndex,
    int CountedActions,
    DateTimeOffset? DeadlineAt,
    DateTimeOffset ServerNow,
    int StateSchemaVersion,
    JsonElement State,
    DateTimeOffset? SettledAt);

// LegalMoveDto: Nước hợp lệ server tính để UI tô ô.
public sealed record LegalMoveDto(Guid PieceId, int FromX, int FromY, int ToX, int ToY, Guid? CapturedPieceId);
// MatchCommandRequest: commandId chống trùng, expectedVersion chống stale, action JSON.
public sealed record MatchCommandRequest(Guid CommandId, int ExpectedVersion, JsonElement Action);
// MatchCommandResultDto: ACK action gồm duplicate flag, sequence, events và snapshot.
public sealed record MatchCommandResultDto(Guid MatchId, Guid CommandId, int SequenceNo, bool Duplicate, JsonElement ResolvedEvents, MatchStateDto Snapshot);
// MatchListItemDto: Một mục lịch sử trận.
public sealed record MatchListItemDto(Guid MatchId, string Mode, string Status, string? Side, string? Result, string? EndReason, DateTimeOffset CreatedAt, DateTimeOffset? EndedAt);
// MatchPageDto: Trang lịch sử cùng cursor tiếp theo.
public sealed record MatchPageDto(IReadOnlyList<MatchListItemDto> Items, string? NextCursor);
// ReplayEntryDto: Một action lịch sử với stateAfter frozen.
public sealed record ReplayEntryDto(int SequenceNo, string Kind, string? ActorSide, JsonElement ResolvedEvents, JsonElement StateAfter, DateTimeOffset CommittedAt);
// ReplayPageDto: Trang replay theo sequence.
public sealed record ReplayPageDto(Guid MatchId, IReadOnlyList<ReplayEntryDto> Entries, int? NextAfterSequence);

// PurchaseHeroDto: Kết quả mua, số coin đã trừ và số dư.
public sealed record PurchaseHeroDto(Guid HeroId, bool Acquired, string CoinBalance, string Charged);
// UpdatePriceRequest: Giá coin dạng chuỗi và lý do chỉnh.
public sealed record UpdatePriceRequest(string CoinPrice, string? Reason = null);
// AdminPriceDto: Xác nhận giá mới và thời điểm thay đổi.
public sealed record AdminPriceDto(Guid Id, string EntityType, string CoinPrice, DateTimeOffset ChangedAt);
// AdminAuditDto: Một bản ghi audit với actor, đích và dữ liệu trước/sau.
public sealed record AdminAuditDto(Guid Id, Guid ActorUserId, string Action, string EntityType, string EntityId,
    JsonElement? BeforeData, JsonElement? AfterData, string? Reason, DateTimeOffset CreatedAt);
// AdminAuditPageDto: Trang audit cùng cursor.
public sealed record AdminAuditPageDto(IReadOnlyList<AdminAuditDto> Items, string? NextCursor);

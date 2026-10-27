// Vai trò file: Các record request/response dùng chung giữa API và client, gần Java record DTO; không có logic truy cập DB.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroChess.Contracts;

// ApiError: Phong bì lỗi gồm code, message, requestId và details.
public sealed record ApiError(string Code, string Message, string? RequestId = null, object? Details = null);

// MeDto: userId/role/status chung; chỉ Player có playerId/isGuest/coinBalance/elo, Admin trả các trường game null.
public sealed record MeDto(Guid UserId, string DisplayName, string Role, string Status, Guid? PlayerId = null, bool? IsGuest = null, string? CoinBalance = null, int? Elo = null,
    string? AvatarName = null, string? FrameName = null, string? AvatarUrl = null);

// UpdateProfileRequest: Sửa hồ sơ hiển thị; field null nghĩa là giữ nguyên giá trị đang lưu.
public sealed record UpdateProfileRequest(string? DisplayName = null, string? AvatarName = null, string? FrameName = null);

// PlayerProfileDto: Hồ sơ công khai của một tài khoản trong trận; không chứa email hay dữ liệu riêng tư.
public sealed record PlayerProfileDto(Guid PlayerId, string DisplayName, int Elo, string? AvatarName, string? FrameName, string? AvatarUrl);

// MatchSidePlayerDto: Một phe trong trận kèm hồ sơ người chơi; phe bot không có hồ sơ.
public sealed record MatchSidePlayerDto(string Side, bool IsBot,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlayerProfileDto? Player);

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
public sealed record TeamSkillDto(Guid Id, string Code, string Name, Guid? FactionId, string ImplementationKey, JsonElement Parameters, JsonElement Eligibility, int? MaxUses, int? CooldownTurns, string? AssetKey, string? Description);
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
// MatchSelectionSideDto: Thông tin selection của một phe; ownLineupId chỉ có ở phe người xem, selectedLineup là đội hình đã chọn cho trận này.
public sealed record MatchSelectionSideDto(string Side, bool Confirmed, bool IsBot,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlayerProfileDto? Player,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? OwnLineupId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? OwnLineupRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MatchSelectedLineupDto? SelectedLineup = null);
// MatchSelectedLineupDto: Đội hình đã chọn cho trận này, không phải mọi đội hình đã lưu.
public sealed record MatchSelectedLineupDto(Guid Id, string Name, int TotalSp,
    IReadOnlyList<MatchSelectedLineupEntryDto> Entries, IReadOnlyList<MatchSelectedLineupSkillDto> Skills);
// MatchSelectedLineupEntryDto: Một hero trong đội hình đã chọn; heroId trùng id trong catalog.
public sealed record MatchSelectedLineupEntryDto(int SlotNo, Guid HeroId);
// MatchSelectedLineupSkillDto: Một command skill đã chọn; skillId trùng id trong catalog.
public sealed record MatchSelectedLineupSkillDto(int SlotNo, Guid SkillId);
// MatchSelectionDto: Selection hai bên và trạng thái trận.
public sealed record MatchSelectionDto(Guid MatchId, string Mode, string Status, IReadOnlyList<MatchSelectionSideDto> Sides);
// WsTicketDto: Vé mở socket một lần cùng hạn dùng.
public sealed record WsTicketDto(string Ticket, DateTimeOffset ExpiresAt);

// MatchStateDto: State JSON kèm version/deadline/serverNow, dấu settlement và hồ sơ người chơi hai phe.
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
    DateTimeOffset? SettledAt,
    IReadOnlyList<MatchSidePlayerDto>? Sides = null);

// LegalMoveDto: Nước hợp lệ server tính để UI tô ô.
public sealed record LegalMoveDto(Guid PieceId, int FromX, int FromY, int ToX, int ToY, Guid? CapturedPieceId);
// MatchCommandRequest: commandId chống trùng, expectedVersion chống stale, action JSON.
public sealed record MatchCommandRequest(Guid CommandId, int ExpectedVersion, JsonElement Action);
// MatchCommandResultDto: ACK action gồm duplicate flag, sequence, events và snapshot.
public sealed record MatchCommandResultDto(Guid MatchId, Guid CommandId, int SequenceNo, bool Duplicate, JsonElement ResolvedEvents, MatchStateDto Snapshot);
// MatchListItemDto: Một mục lịch sử trận kèm hồ sơ hai phe, đổi Elo, thưởng, thời lượng và cờ replay sẵn có.
public sealed record MatchListItemDto(Guid MatchId, string Mode, string Status, string? Side, string? Result, string? EndReason,
    DateTimeOffset CreatedAt, DateTimeOffset? EndedAt,
    string? Outcome = null, Guid? OpponentPlayerId = null, string? OpponentDisplayName = null,
    int? EloBefore = null, int? EloAfter = null, int? EloDelta = null,
    double? DurationSeconds = null, bool ReplayAvailable = false,
    int RatingChange = 0, int Bonus = 0, IReadOnlyList<MatchSidePlayerDto>? Sides = null);
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

// RankingEntryDto: Một dòng bảng xếp hạng; chỉ có dữ liệu công khai, không lộ email hay thông tin nhạy cảm.
public sealed record RankingEntryDto(int Rank, Guid PlayerId, string DisplayName, int Rating,
    int GamesPlayed, int Wins, int Draws, int Losses);
// RankingPageDto: Trang bảng xếp hạng cùng cursor tiếp theo (keyset theo rating rồi playerId).
public sealed record RankingPageDto(IReadOnlyList<RankingEntryDto> Items, string? NextCursor, int Total);
// MyRankingDto: Thông tin xếp hạng của người chơi hiện tại; null khi chưa có hồ sơ rating.
public sealed record MyRankingDto(int? Rank, Guid PlayerId, string DisplayName, int Rating,
    int GamesPlayed, int Wins, int Draws, int Losses, int Total);
// MatchDetailDto: Chi tiết một trận đã kết thúc: hai bên, kết quả, Elo, thời lượng, replay và lineup đã đóng băng.
public sealed record MatchDetailDto(Guid MatchId, string Mode, string Status, string? Result, string? EndReason,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt,
    IReadOnlyList<MatchSideDto> Sides, bool ReplayAvailable, int ActionCount);
// MatchSideDto: Một bên trong trận kèm hồ sơ người chơi, kết quả và lineup đã dùng lúc đấu.
public sealed record MatchSideDto(string Side, Guid? PlayerId, string? DisplayName, string ParticipantType,
    string? Outcome, int? EloBefore, int? EloAfter, long CoinReward, MatchSideLineupDto Lineup,
    bool IsBot = false, PlayerProfileDto? Player = null);
// MatchLineupDto: Lineup của các bên trong trận; bên đối thủ chỉ hiện khi trận đã bắt đầu.
public sealed record MatchLineupDto(Guid MatchId, IReadOnlyList<MatchSideLineupDto> Sides);
// MatchSideLineupDto: Lineup đã đóng băng của một bên.
public sealed record MatchSideLineupDto(string Side, IReadOnlyList<MatchLineupEntryDto> Entries, IReadOnlyList<MatchLineupSkillDto> Skills);
// MatchLineupEntryDto: Một hero trong lineup snapshot của trận; dữ liệu đóng băng tại thời điểm trận.
public sealed record MatchLineupEntryDto(int SlotNo, Guid HeroId, string? HeroCode, string? HeroName,
    string ClassCode, int SetupPoints, string? FactionCode, int StartX, int StartY, Guid? CosmeticId);
// MatchLineupSkillDto: Command skill đã trang bị tại thời điểm trận.
public sealed record MatchLineupSkillDto(int SlotNo, Guid SkillId, string? Code, string? Name, string? ImplementationKey);

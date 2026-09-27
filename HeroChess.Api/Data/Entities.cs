// Vai trò file: Các object ánh xạ DB (gần JPA entity); không phải DTO trả mạng hoặc state bàn cờ. Mapping nằm ở AppDbContext.
using System.Text.Json;

namespace HeroChess.Api.Data;

// User: Tài khoản chung. Identity tạo User tạm khi đăng ký; store luôn lưu Player hoặc Admin cụ thể.
// Role là discriminator EF (TPH), không nhận quyền từ request. Không lưu trực tiếp User chưa phân loại.
public class User
{
    public User() { }
    protected User(string role) => Role = role;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? UserName { get; set; }
    public string? NormalizedUserName { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public string? PasswordHash { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public string Role { get; private set; } = "unassigned";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// Player: Loại User được chơi; ví/Elo/ownership vẫn là các entity nghiệp vụ riêng.
public sealed class Player() : User("player")
{
    public bool IsGuest { get; set; }
}

// Admin: Loại User chỉ quản trị; không được cấp ví, Elo, hero hoặc quyền chơi.
public sealed class Admin() : User("admin");

// AuthIdentity: Nối user với provider/subject đăng nhập; không lưu mật khẩu.
public sealed class AuthIdentity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "";
    public string Subject { get; set; } = "";
}

// PlayerWallet: Số dư coin hiện tại của player; cập nhật cùng ledger trong transaction.
public sealed class PlayerWallet { public Guid PlayerId { get; set; } public long Balance { get; set; } }
// PlayerRating: Elo và thống kê ranked; khác với độ khó của bot.
public sealed class PlayerRating { public Guid PlayerId { get; set; } public int Elo { get; set; } public int GamesPlayed { get; set; } public int Wins { get; set; } public int Draws { get; set; } public int Losses { get; set; } }
// Ruleset: Cấu hình bộ luật trong catalog: budget, thời gian lượt, giới hạn action.
public sealed class Ruleset { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public short SetupBudget { get; set; } public short TurnSeconds { get; set; } public int ActionLimit { get; set; } public JsonDocument Config { get; set; } = JsonDocument.Parse("{}"); public bool IsActive { get; set; } }
// ChessClass: Loại quân cơ bản (GENERAL/ROOK/...) và số lượng cần trong lineup.
public sealed class ChessClass { public string Code { get; set; } = ""; public string NameVi { get; set; } = ""; public short BaseSp { get; set; } public short RequiredCount { get; set; } public string BaseMovementCode { get; set; } = ""; }
// LineupSlot: Vị trí chuẩn của từng slot và class được phép đặt.
public sealed class LineupSlot { public short SlotNo { get; set; } public string ClassCode { get; set; } = ""; public short StartX { get; set; } public short StartY { get; set; } }
// HistoricalCharacter: Nhân vật lịch sử chung để cấm chọn hai biến thể cùng nhân vật.
public sealed class HistoricalCharacter { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? Description { get; set; } }
// HeroTrait: Định nghĩa trait riêng cho tối đa một hero: tên/kind/parameters riêng; implementation key có thể dùng chung, chưa tự thực thi effect.
public sealed class HeroTrait { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string Kind { get; set; } = ""; public string ImplementationKey { get; set; } = ""; public JsonDocument Parameters { get; set; } = JsonDocument.Parse("{}"); public string? Description { get; set; } }
// Hero: Nội dung hero trong catalog: class, SP, trait, giá và cờ bật/fixture.
public sealed class Hero { public Guid Id { get; set; } public string Code { get; set; } = ""; public Guid? CharacterId { get; set; } public string ClassCode { get; set; } = ""; public Guid? TraitId { get; set; } public string Name { get; set; } = ""; public short SetupPoints { get; set; } public long CoinPrice { get; set; } public bool IsStarter { get; set; } public bool IsEnabled { get; set; } public bool IsTestFixture { get; set; } public string? AssetKey { get; set; } public string? Description { get; set; } public HeroTrait? Trait { get; set; } }
// Faction: Danh mục phe; ngưỡng skill cụ thể còn chờ thiết kế.
public sealed class Faction { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? Description { get; set; } }
// HeroFaction: Liên kết hero–phe; schema hiện cho nhiều link, luật thiết kế mới chỉ cho một phe mỗi hero.
public sealed class HeroFaction { public Guid HeroId { get; set; } public Guid FactionId { get; set; } }
// TeamSkill: Định nghĩa command/team skill, eligibility và cooldown/charge; dữ liệu không đồng nghĩa handler đã có.
public sealed class TeamSkill { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public Guid? FactionId { get; set; } public string ImplementationKey { get; set; } = ""; public JsonDocument Parameters { get; set; } = JsonDocument.Parse("{}"); public JsonDocument Eligibility { get; set; } = JsonDocument.Parse("{}"); public short? MaxUses { get; set; } public short? CooldownTurns { get; set; } public bool IsEnabled { get; set; } public bool IsTestFixture { get; set; } public string? AssetKey { get; set; } public string? Description { get; set; } }
// Cosmetic: Nội dung skin gắn với hero và giá coin.
public sealed class Cosmetic { public Guid Id { get; set; } public string Code { get; set; } = ""; public Guid HeroId { get; set; } public string Name { get; set; } = ""; public long CoinPrice { get; set; } public string AssetKey { get; set; } = ""; public bool IsEnabled { get; set; } }
// PlayerHero: Ownership hero của player; lưu thêm key mua để retry kể cả giá 0.
public sealed class PlayerHero { public Guid PlayerId { get; set; } public Guid HeroId { get; set; } public string AcquiredVia { get; set; } = ""; public DateTimeOffset AcquiredAt { get; set; } public string? IdempotencyKey { get; set; } }
// PlayerCosmetic: Ownership skin; không phải bản định nghĩa skin.
public sealed class PlayerCosmetic { public Guid PlayerId { get; set; } public Guid CosmeticId { get; set; } public DateTimeOffset AcquiredAt { get; set; } }
// SavedLineup: Đội hình có thể sửa của player, gồm revision và entry/skill con.
public sealed class SavedLineup { public Guid Id { get; set; } public Guid PlayerId { get; set; } public Guid RulesetId { get; set; } public string Name { get; set; } = ""; public int Revision { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } public List<SavedLineupEntry> Entries { get; set; } = new(); public List<SavedLineupSkill> Skills { get; set; } = new(); }
// SavedLineupEntry: Một slot chọn hero và cosmetic trong đội hình lưu.
public sealed class SavedLineupEntry { public Guid LineupId { get; set; } public short SlotNo { get; set; } public string ClassCode { get; set; } = ""; public Guid HeroId { get; set; } public Guid? CosmeticId { get; set; } }
// SavedLineupSkill: Một slot skill đã chọn trong đội hình lưu.
public sealed class SavedLineupSkill { public Guid LineupId { get; set; } public short SlotNo { get; set; } public Guid TeamSkillId { get; set; } }
// GameMatch: Metadata trận và ruleset frozen; không chứa trực tiếp danh sách quân hiện tại.
public sealed class GameMatch { public Guid Id { get; set; } public string Mode { get; set; } = ""; public string Status { get; set; } = "selecting"; public Guid RulesetId { get; set; } public JsonDocument RulesetSnapshot { get; set; } = JsonDocument.Parse("{}"); public string ContentVersion { get; set; } = "development"; public string? Result { get; set; } public string? EndReason { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? StartedAt { get; set; } public DateTimeOffset? EndedAt { get; set; } public DateTimeOffset? SettledAt { get; set; } public List<MatchParticipant> Participants { get; set; } = new(); public MatchState? State { get; set; } }
// MatchParticipant: Một bên Red/Black: human có PlayerId, bot có BotConfig; lưu lineup snapshot và kết quả thưởng.
public sealed class MatchParticipant { public Guid MatchId { get; set; } public string Side { get; set; } = ""; public Guid? PlayerId { get; set; } public string ParticipantType { get; set; } = "human"; public JsonDocument? BotConfig { get; set; } public Guid? SourceLineupId { get; set; } public int? SourceLineupRevision { get; set; } public JsonDocument LineupSnapshot { get; set; } = JsonDocument.Parse("{}"); public DateTimeOffset? ConfirmedAt { get; set; } public int? EloBefore { get; set; } public int? EloAfter { get; set; } public long CoinReward { get; set; } public JsonDocument Stats { get; set; } = JsonDocument.Parse("{}"); }
// MatchState: Snapshot hiện tại được lưu DB, version và deadline; khác GameState thuần luật.
public sealed class MatchState { public Guid MatchId { get; set; } public int Version { get; set; } public string SideToMove { get; set; } = "red"; public int TurnIndex { get; set; } public int CountedActions { get; set; } public DateTimeOffset? TurnDeadlineAt { get; set; } public int StateSchemaVersion { get; set; } = 1; public JsonDocument State { get; set; } = JsonDocument.Parse("{}"); public DateTimeOffset UpdatedAt { get; set; } }
// MatchAction: Action đã chấp nhận, commandId, sequence, events và stateAfter; nguồn replay.
public sealed class MatchAction { public Guid Id { get; set; } public Guid MatchId { get; set; } public int SequenceNo { get; set; } public Guid CommandId { get; set; } public string? ActorSide { get; set; } public string Kind { get; set; } = ""; public JsonDocument RequestPayload { get; set; } = JsonDocument.Parse("{}"); public JsonDocument ResolvedEvents { get; set; } = JsonDocument.Parse("[]"); public JsonDocument StateAfter { get; set; } = JsonDocument.Parse("{}"); public int StateSchemaVersion { get; set; } = 1; public DateTimeOffset ReceivedAt { get; set; } public DateTimeOffset CommittedAt { get; set; } }
// CoinTransaction: Ledger thay đổi coin để truy vết/dedup; không phải số dư hiện tại.
public sealed class CoinTransaction { public Guid Id { get; set; } public Guid PlayerId { get; set; } public string IdempotencyKey { get; set; } = ""; public string Kind { get; set; } = ""; public long Amount { get; set; } public long BalanceAfter { get; set; } public Guid? MatchId { get; set; } public string? MatchSide { get; set; } public Guid? HeroId { get; set; } public Guid? CosmeticId { get; set; } public DateTimeOffset CreatedAt { get; set; } }
// AdminAuditLog: Ai sửa nội dung nào, trước/sau ra sao và lý do; hiện dùng cho chỉnh giá.
public sealed class AdminAuditLog { public Guid Id { get; set; } public Guid ActorUserId { get; set; } public string Action { get; set; } = ""; public string EntityType { get; set; } = ""; public string EntityId { get; set; } = ""; public JsonDocument? BeforeData { get; set; } public JsonDocument? AfterData { get; set; } public string? Reason { get; set; } public DateTimeOffset CreatedAt { get; set; } }

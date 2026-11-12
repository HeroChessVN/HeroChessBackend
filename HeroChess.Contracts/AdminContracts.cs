// Vai trò file: DTO dùng chung cho toàn bộ API Admin (Hero, Shop, Player, Match, Analytics, Gift Code, Quyền). Chỉ là dữ liệu, không có logic.
using System.Text.Json;

namespace HeroChess.Contracts;

// AdminPermissions: Danh sách quyền fine-grained; "*" là wildcard cho admin toàn quyền.
public static class AdminPermissions
{
    public const string Read = "read";
    public const string Hero = "hero";
    public const string Shop = "shop";
    public const string Player = "player";
    public const string GiftCode = "giftcode";
    public const string Analytics = "analytics";
    public const string Ruleset = "ruleset";
    public const string Audit = "audit";
    public const string Admin = "admin";
    public const string Wildcard = "*";

    public static readonly string[] All =
    [
        Read, Hero, Shop, Player, GiftCode, Analytics, Ruleset, Audit, Admin
    ];

    // Describe: Mô tả quyền để AdminWeb hiển thị checklist phân quyền.
    public static IReadOnlyList<AdminPermissionDto> Describe() =>
    [
        new(Read, "Xem dashboard tổng quan. Không mở danh sách người chơi hay dữ liệu PII."),
        new(Hero, "Tạo, sửa, bật/tắt hero, trait, phe và skill."),
        new(Shop, "Quản lý giá và tính khả dụng trong cửa hàng."),
        new(Player, "Xem, ban/mở khoá, điều chỉnh ví và bộ sưu tập của người chơi."),
        new(GiftCode, "Quản lý gift code và lịch sử quy đổi."),
        new(Analytics, "Đọc số liệu cân bằng, bot, trận đấu và bộ luật."),
        new(Ruleset, "Xem bộ luật và kịch bản (cũng dùng cho màn hình trận đấu)."),
        new(Audit, "Đọc nhật ký thao tác admin."),
        new(Admin, "Quản lý tài khoản admin và quyền của họ.")
    ];
}

public sealed record AdminPermissionDto(string Key, string Description);

// ---------- Hero ----------

// AdminHeroDto: Hero ở góc nhìn admin: có cả hero đang tắt và cờ shop.
public sealed record AdminHeroDto(Guid Id, string Code, string Name, string ClassCode, Guid? CharacterId, int SetupPoints,
    string CoinPrice, bool IsEnabled, bool IsInShop, bool IsStarter, bool IsTestFixture, string? AssetKey, string? Description,
    IReadOnlyList<string> FactionCodes, Guid? TraitId, string? TraitCode);

// AdminHeroPageDto: Trang danh sách hero kèm tổng số bản ghi.
public sealed record AdminHeroPageDto(IReadOnlyList<AdminHeroDto> Items, string? NextCursor, int Total);

// AdminCosmeticDto: Skin của hero kèm giá và trạng thái bật/tắt.
public sealed record AdminCosmeticDto(Guid Id, string Code, string Name, Guid HeroId, string CoinPrice, bool IsEnabled, string? AssetKey);

// AdminPriceChangeDto: Một lần đổi giá lấy từ nhật ký admin.
public sealed record AdminPriceChangeDto(Guid Id, Guid ActorUserId, string CoinPriceBefore, string CoinPriceAfter, string? Reason, DateTimeOffset ChangedAt);

// AdminHeroDetailDto: Chi tiết hero: thông tin, skin, số chủ sở hữu, doanh thu và lịch sử đổi giá.
public sealed record AdminHeroDetailDto(AdminHeroDto Hero, IReadOnlyList<AdminCosmeticDto> Cosmetics, IReadOnlyList<AdminSkillDto> Skills,
    int OwnerCount, long TotalRevenue, IReadOnlyList<AdminPriceChangeDto> PriceHistory);

// AdminHeroUpsertRequest: Tạo hero mới; các trường null/optional được kiểm tra server-side.
public sealed record AdminHeroUpsertRequest(string? Code, string Name, string ClassCode, int SetupPoints, string? CoinPrice,
    bool? IsEnabled, bool? IsInShop, bool? IsStarter, string? AssetKey, string? Description, Guid? TraitId, string? Reason);

// AdminHeroUpdateRequest: Sửa hero; trường null nghĩa là giữ nguyên.
public sealed record AdminHeroUpdateRequest(string? Name, int? SetupPoints, bool? IsEnabled, string? AssetKey, string? Description,
    Guid? TraitId, string? Reason);

// AdminHeroFactionRequest: Gán phe cho hero (thiết kế: tối đa một phe mỗi hero).
public sealed record AdminHeroFactionRequest(Guid? FactionId, string? Reason);

// AdminSkillDto: Command skill ở góc nhìn admin kèm trạng thái draft/live.
public sealed record AdminSkillDto(Guid Id, string Code, string Name, Guid? FactionId, string? FactionCode, string ImplementationKey,
    bool IsEnabled, int? MaxUses, int? CooldownTurns, string? Description, string Status);

// AdminSkillUpdateRequest: Sửa metadata của skill; không cho sửa hiệu ứng khi chưa có luật thiết kế.
public sealed record AdminSkillUpdateRequest(string? Name, string? Description, bool? IsEnabled, int? MaxUses, int? CooldownTurns, string? Reason);

// AdminFactionDto: Phe kèm số hero và số skill thuộc phe.
public sealed record AdminFactionDto(Guid Id, string Code, string Name, string? Description, int HeroCount, int SkillCount);

// AdminFactionUpsertRequest: Tạo phe mới.
public sealed record AdminFactionUpsertRequest(string? Code, string Name, string? Description, string? Reason);

// AdminFactionUpdateRequest: Sửa phe.
public sealed record AdminFactionUpdateRequest(string? Name, string? Description, string? Reason);

// AdminRulesetDto: Bộ luật kèm số trận đã dùng; chỉ đọc, không sửa từ AdminWeb.
public sealed record AdminRulesetDto(Guid Id, string Code, string Name, int SetupBudget, int TurnSeconds, int ActionLimit,
    bool IsActive, JsonElement Config, int MatchCount, DateTimeOffset? LastUsedAt);

// ---------- Shop ----------

// AdminShopHeroDto: Hero trong cửa hàng kèm cờ khả dụng và số lượt đã mua.
public sealed record AdminShopHeroDto(AdminHeroDto Hero, bool Purchasable, long PurchaseCount, DateTimeOffset? LastPurchasedAt);

// AdminShopHeroPageDto: Trang danh sách cửa hàng.
public sealed record AdminShopHeroPageDto(IReadOnlyList<AdminShopHeroDto> Items, string? NextCursor, int Total);

// AdminShopUpdateRequest: Sửa giá và khả dụng trong cửa hàng.
public sealed record AdminShopUpdateRequest(string? CoinPrice, bool? IsInShop, string? Reason);

// AdminBatchPriceItem: Một dòng trong yêu cầu đổi giá hàng loạt.
public sealed record AdminBatchPriceItem(Guid HeroId, string CoinPrice);

// AdminBatchPriceRequest: Đổi giá nhiều hero trong một lần gọi.
public sealed record AdminBatchPriceRequest(IReadOnlyList<AdminBatchPriceItem> Items, string? Reason);

// AdminBatchPriceItemResult: Kết quả từng dòng đổi giá; dòng lỗi không làm hỏng các dòng còn lại.
public sealed record AdminBatchPriceItemResult(Guid HeroId, bool Succeeded, string? CoinPrice, string? ErrorCode, string? ErrorMessage);

// AdminBatchPriceResultDto: Kết quả đổi giá hàng loạt.
public sealed record AdminBatchPriceResultDto(IReadOnlyList<AdminBatchPriceItemResult> Results);

// AdminPurchaseDto: Một giao dịch mua hero trong cửa hàng.
public sealed record AdminPurchaseDto(Guid Id, Guid PlayerId, string? PlayerDisplayName, Guid HeroId, string? HeroCode,
    long Amount, DateTimeOffset CreatedAt);

// AdminPurchasePageDto: Trang lịch sử mua.
public sealed record AdminPurchasePageDto(IReadOnlyList<AdminPurchaseDto> Items, string? NextCursor, int Total);

// ---------- Player ----------

// AdminPlayerDto: Tài khoản người chơi ở góc nhìn admin (không có password/secret).
public sealed record AdminPlayerDto(Guid Id, string DisplayName, string Status, string? Email, int Elo, int GamesPlayed,
    int Wins, int Draws, int Losses, string CoinBalance, int OwnedHeroes, DateTimeOffset CreatedAt);

// AdminPlayerPageDto: Trang danh sách người chơi.
public sealed record AdminPlayerPageDto(IReadOnlyList<AdminPlayerDto> Items, string? NextCursor, int Total);

// AdminOwnedHeroDto: Hero người chơi đang sở hữu kèm nguồn nhận.
public sealed record AdminOwnedHeroDto(Guid HeroId, string HeroCode, string HeroName, string AcquiredVia, DateTimeOffset AcquiredAt);

// AdminLineupSummaryDto: Đội hình đã lưu (tóm tắt).
public sealed record AdminLineupSummaryDto(Guid Id, string Name, int Revision, int EntryCount, DateTimeOffset UpdatedAt);

// AdminModerationDto: Một lần ban/mở khoá hoặc điều chỉnh của admin.
public sealed record AdminModerationDto(Guid Id, Guid ActorUserId, string Action, string? Reason, DateTimeOffset CreatedAt);

// AdminPlayerMatchDto: Trận đấu của người chơi.
public sealed record AdminPlayerMatchDto(Guid MatchId, string Mode, string Status, string? Result, DateTimeOffset CreatedAt);

// AdminPlayerDetailDto: Hồ sơ đầy đủ của một người chơi.
public sealed record AdminPlayerDetailDto(AdminPlayerDto Player, IReadOnlyList<AdminOwnedHeroDto> Heroes,
    IReadOnlyList<AdminLineupSummaryDto> Lineups, IReadOnlyList<AdminModerationDto> Moderation, IReadOnlyList<AdminPlayerMatchDto> Matches);

// AdminPlayerStatusRequest: Ban/mở khoá tài khoản; lý do bắt buộc.
public sealed record AdminPlayerStatusRequest(string Status, string Reason);

// AdminCoinAdjustmentRequest: Cộng/trừ coin thủ công.
public sealed record AdminCoinAdjustmentRequest(string Amount, string Reason);

// AdminCoinAdjustmentDto: Kết quả điều chỉnh ví.
public sealed record AdminCoinAdjustmentDto(Guid PlayerId, string BalanceBefore, string BalanceAfter, string Amount);

// AdminGrantHeroRequest: Cấp hero cho người chơi.
public sealed record AdminGrantHeroRequest(Guid HeroId, string? Reason);

// AdminWalletTransactionDto: Một dòng ledger của ví người chơi.
public sealed record AdminWalletTransactionDto(Guid Id, string Kind, long Amount, string BalanceAfter, Guid? HeroId,
    Guid? MatchId, DateTimeOffset CreatedAt);

// ---------- Match ----------

// AdminMatchSideDto: Một phe trong trận ở góc nhìn admin.
public sealed record AdminMatchSideDto(string Side, Guid? PlayerId, string? DisplayName, string ParticipantType, bool IsBot,
    string? Difficulty, string? Outcome, int? EloBefore, int? EloAfter, long CoinReward, int HeroCount);

// AdminMatchDto: Trận ở góc nhìn admin.
public sealed record AdminMatchDto(Guid Id, string Mode, string Status, string? Result, string? EndReason, Guid RulesetId,
    string? RulesetCode, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, DateTimeOffset? SettledAt,
    int ActionCount, IReadOnlyList<AdminMatchSideDto> Sides);

// AdminMatchPageDto: Trang danh sách trận.
public sealed record AdminMatchPageDto(IReadOnlyList<AdminMatchDto> Items, string? NextCursor, int Total);

// AdminMatchHeroPickDto: Một quân trong lineup đã đóng băng của phe.
public sealed record AdminMatchHeroPickDto(int SlotNo, Guid HeroId, string? HeroCode, string? HeroName, string ClassCode,
    int SetupPoints, string? FactionCode, int StartX, int StartY);

// AdminMatchSkillPickDto: Một command skill đã trang bị.
public sealed record AdminMatchSkillPickDto(int SlotNo, Guid SkillId, string? Code, string? Name);

// AdminMatchSideLineupDto: Lineup đã đóng băng của một phe.
public sealed record AdminMatchSideLineupDto(string Side, IReadOnlyList<AdminMatchHeroPickDto> Heroes, IReadOnlyList<AdminMatchSkillPickDto> Skills);

// AdminMatchDetailDto: Chi tiết trận: snapshot luật, lineup hai phe, hành động và trạng thái settlement.
public sealed record AdminMatchDetailDto(AdminMatchDto Match, JsonElement RulesetSnapshot, string ContentVersion,
    IReadOnlyList<AdminMatchSideLineupDto> Lineups, bool HasReplay, DateTimeOffset? FirstActionAt);

// AdminMatchActionDto: Một hành động trong nhật ký trận.
public sealed record AdminMatchActionDto(int SequenceNo, string Kind, string? ActorSide, JsonElement Request, JsonElement Events,
    DateTimeOffset CommittedAt);

// ---------- Analytics ----------

// AdminStatsDto: Số liệu tổng quan cho dashboard.
public sealed record AdminStatsDto(int TotalPlayers, int ActivePlayers, int DisabledPlayers, int TotalHeroes, int EnabledHeroes,
    int InShopHeroes, int TotalMatches, int CompletedMatches, int RankedMatches, int BotMatches, int ActiveMatches,
    long TotalCoinBalance, int AdminCount, DateTimeOffset GeneratedAt);

// AdminMetricSampleDto: Một metric kèm cỡ mẫu; null nghĩa là chưa đủ dữ liệu.
public sealed record AdminMetricSampleDto(double? Value, int SampleSize);

// AdminHeroWinRateDto: Tỉ lệ chọn/thắng của hero; WinRate null khi chưa có trận kết thúc.
public sealed record AdminHeroWinRateDto(Guid HeroId, string HeroCode, string HeroName, string? FactionCode, int Picks,
    int Matches, int Wins, int Draws, int Losses, double? WinRate, double? PickRate);

// AdminFactionUsageDto: Hiệu suất theo phe.
public sealed record AdminFactionUsageDto(string FactionCode, int Matches, int Wins, int Draws, int Losses, double? WinRate);

// AdminSkillUsageDto: Tần suất chọn skill.
public sealed record AdminSkillUsageDto(Guid SkillId, string SkillCode, string SkillName, int Matches, int Uses);

// AdminBalanceAnalyticsDto: Báo cáo cân bằng, tách riêng ranked và bot.
public sealed record AdminBalanceAnalyticsDto(string Mode, int MatchCount, int PlayerMatchCount, AdminMetricSampleDto FirstMoveAdvantage,
    double? AverageDurationSeconds, IReadOnlyList<AdminHeroWinRateDto> Heroes, IReadOnlyList<AdminFactionUsageDto> Factions,
    IReadOnlyList<AdminSkillUsageDto> Skills);

// AdminBotDifficultyDto: Hiệu suất bot theo độ khó.
public sealed record AdminBotDifficultyDto(string Difficulty, int Matches, int Wins, int Draws, int Losses, double? WinRate);

// AdminBotRulesetDto: Hiệu suất bot theo bộ luật.
public sealed record AdminBotRulesetDto(Guid RulesetId, string? RulesetCode, int Matches, int Wins, int Draws, int Losses, double? WinRate);

// AdminBotAnalyticsDto: Báo cáo bot, tách khỏi cân bằng PvP.
public sealed record AdminBotAnalyticsDto(int BotMatches, int BotWins, int PlayerWins, int Draws, double? BotWinRate,
    IReadOnlyList<AdminBotDifficultyDto> ByDifficulty, IReadOnlyList<AdminBotRulesetDto> ByRuleset);

// ---------- Gift code ----------

// AdminGiftCodeDto: Một gift code kèm số lượt đã quy đổi.
public sealed record AdminGiftCodeDto(Guid Id, string Code, string RewardKind, int RewardQuantity, Guid? RewardHeroId,
    Guid? RewardCosmeticId, DateTimeOffset StartsAt, DateTimeOffset? ExpiresAt, int MaxRedemptions, int MaxPerPlayer,
    bool IsActive, int RedemptionCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// AdminGiftCodePageDto: Trang danh sách gift code.
public sealed record AdminGiftCodePageDto(IReadOnlyList<AdminGiftCodeDto> Items, string? NextCursor, int Total);

// AdminGiftCodeUpsertRequest: Tạo gift code; để trống Code để server sinh mã.
public sealed record AdminGiftCodeUpsertRequest(string? Code, string RewardKind, int RewardQuantity, Guid? RewardHeroId,
    Guid? RewardCosmeticId, DateTimeOffset? StartsAt, DateTimeOffset? ExpiresAt, int MaxRedemptions, int MaxPerPlayer,
    bool? IsActive, string? Reason);

// AdminGiftCodeUpdateRequest: Sửa cấu hình gift code.
public sealed record AdminGiftCodeUpdateRequest(bool? IsActive, DateTimeOffset? ExpiresAt, int? MaxRedemptions, int? MaxPerPlayer, string? Reason);

// AdminGiftCodeRedemptionDto: Một lần quy đổi gift code.
public sealed record AdminGiftCodeRedemptionDto(Guid Id, Guid PlayerId, string? PlayerDisplayName, string RewardKind,
    int RewardQuantity, long? CoinBalanceAfter, DateTimeOffset RedeemedAt);

// AdminGiftCodeRedemptionPageDto: Trang lịch sử quy đổi.
public sealed record AdminGiftCodeRedemptionPageDto(IReadOnlyList<AdminGiftCodeRedemptionDto> Items, string? NextCursor, int Total);

// RedeemGiftCodeRequest: Player nhập mã gift code.
public sealed record RedeemGiftCodeRequest(string Code);

// RedeemGiftCodeDto: Kết quả quy đổi gift code cho người chơi.
public sealed record RedeemGiftCodeDto(string Code, string RewardKind, int RewardQuantity, Guid? HeroId, Guid? CosmeticId, string CoinBalance);

// ---------- Admin account ----------

// AdminAccountDto: Tài khoản admin và danh sách quyền đang có.
public sealed record AdminAccountDto(Guid Id, string DisplayName, string? Email, string Status, IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAt);

// AdminAccountPageDto: Danh sách tài khoản admin.
public sealed record AdminAccountPageDto(IReadOnlyList<AdminAccountDto> Items);

// AdminCreateAccountRequest: Tạo tài khoản admin mới (mật khẩu do admin chọn, phải đạt luật Identity).
public sealed record AdminCreateAccountRequest(string Email, string DisplayName, string Password, IReadOnlyList<string>? Permissions);

// AdminUpdatePermissionsRequest: Thay toàn bộ quyền của một admin.
public sealed record AdminUpdatePermissionsRequest(IReadOnlyList<string> Permissions);

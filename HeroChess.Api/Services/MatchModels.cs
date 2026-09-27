// Vai trò file: Snapshot nội dung đã đóng băng và options của matchmaking/runtime; khác với DTO trả client.
using System.Text.Json;

namespace HeroChess.Api.Services;

// FrozenLineup: Bản sao lineup dùng trong trận; sửa/xóa lineup nguồn không sửa snapshot.
public sealed record FrozenLineup(Guid LineupId, int Revision, int TotalSp, IReadOnlyList<FrozenPiece> Pieces, IReadOnlyList<FrozenSkill> Skills);
// FrozenPiece: Hero/class/SP/vị trí/trait key đã đóng băng cho một slot.
public sealed record FrozenPiece(int SlotNo, Guid HeroId, string ClassCode, int SetupPoints, int StartX, int StartY, string? MovementImplementationKey, Guid? CosmeticId,
    string? TraitKind = null, string? TraitImplementationKey = null);
// FrozenSkill: Skill key, charge và cooldown đã đóng băng; chưa thực thi gameplay.
public sealed record FrozenSkill(int SlotNo, Guid SkillId, string ImplementationKey, int? MaxUses, int CooldownTurns);
// RulesetSnapshot: Bộ luật và policy thưởng/Elo của trận, giữ đúng giá trị dù config sau này đổi.
public sealed record RulesetSnapshot(string Code, int SetupBudget, int TurnSeconds, int ActionLimit, JsonElement Config,
    string RatingPolicyVersion, string RewardPolicyVersion, int EloKFactor, int WinnerCoinReward, int DrawCoinReward);

// MatchmakingOptions: Cửa sổ Elo cho ghép ranked.
public sealed class MatchmakingOptions
{
    public int EloWindow { get; set; } = 200;
}

// MatchRuntimeOptions: Options vé WS, giới hạn message, worker poll, reward/K, budget bot và timeout selection.
public sealed class MatchRuntimeOptions
{
    public int WebSocketTicketSeconds { get; set; } = 30;
    public int MaxWebSocketMessageBytes { get; set; } = 64 * 1024;
    public int TimeoutPollMilliseconds { get; set; } = 500;
    public int WinnerCoinReward { get; set; } = 10;
    public int DrawCoinReward { get; set; } = 5;
    public int EloKFactor { get; set; } = 32;
    public int BotThinkMilliseconds { get; set; } = 250;
    public int SelectionSeconds { get; set; } = 120;
}

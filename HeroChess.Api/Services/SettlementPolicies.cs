// Vai trò file: Policy tính coin và Elo thuần tính toán; service settlement chịu trách nhiệm lưu DB.
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Services;

// RewardPolicy: Công thức thưởng coin; prototype chỉ thưởng ranked completed.
public sealed class RewardPolicy(IOptions<MatchRuntimeOptions> options)
{
    public string Version => "dev-reward-v1";
    // Reward: Tính thưởng theo mode/status/result/side; ưu tiên giá trị frozen truyền vào, 0 là hợp lệ.
    public long Reward(string mode, string status, string? result, string side, int? winnerReward = null, int? drawReward = null) =>
        mode == "ranked" && status == "completed"
            ? result == side + "_win" ? winnerReward ?? options.Value.WinnerCoinReward : result == "draw" ? drawReward ?? options.Value.DrawCoinReward : 0
            : 0;
}

// RatingPolicy: Công thức Elo, không tự truy vấn hoặc ghi rating.
public sealed class RatingPolicy(IOptions<MatchRuntimeOptions> options)
{
    public string Version => $"elo-v1-k{options.Value.EloKFactor}";
    // Calculate: Tính Elo mới từ hai rating, score 0/0.5/1 và K; làm tròn và chặn Elo âm.
    public int Calculate(int ownElo, int opponentElo, double score, int? kFactor = null)
    {
        var expected = 1d / (1d + Math.Pow(10d, (opponentElo - ownElo) / 400d));
        return Math.Max(0, ownElo + (int)Math.Round((kFactor ?? options.Value.EloKFactor) * (score - expected)));
    }
}

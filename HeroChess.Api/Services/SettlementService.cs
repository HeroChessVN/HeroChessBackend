// Vai trò file: Chốt thưởng/rating đúng một lần; là transaction riêng sau khi kết quả trận đã commit.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Services;

public sealed class SettlementService(AppDbContext db, TimeProvider clock, MatchmakingService matchmaking,
    RewardPolicy rewards, RatingPolicy ratingPolicy, MatchConnectionHub hub)
{
    // SettleAsync: Lock match/ví/rating theo thứ tự, dùng policy frozen kể cả 0; ghi ledger/stats/settledAt nguyên tử rồi release reservation và enqueue settled.
    public async Task SettleAsync(Guid matchId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var match = await db.Matches.FromSqlInterpolated($"SELECT * FROM hero_chess.game_match WHERE id={matchId} FOR UPDATE")
            .Include(x => x.Participants).SingleAsync(ct);
        if (match.Status is not ("completed" or "cancelled") || match.SettledAt is not null) { await transaction.CommitAsync(ct); return; }
        var humans = match.Participants.Where(x => x.PlayerId is not null).OrderBy(x => x.PlayerId).ToArray();
        var playerIds = humans.Select(x => x.PlayerId!.Value).ToArray();
        var wallets = await db.Wallets.FromSqlInterpolated($"SELECT * FROM hero_chess.player_wallet WHERE player_id = ANY({playerIds}) ORDER BY player_id FOR UPDATE").ToDictionaryAsync(x => x.PlayerId, ct);
        var ratings = await db.Ratings.FromSqlInterpolated($"SELECT * FROM hero_chess.player_rating WHERE player_id = ANY({playerIds}) ORDER BY player_id FOR UPDATE").ToDictionaryAsync(x => x.PlayerId, ct);
        var startingElo = ratings.ToDictionary(x => x.Key, x => x.Value.Elo);
        var frozenPolicy = GameJson.Read<RulesetSnapshot>(match.RulesetSnapshot);
        var isRankedResult = match.Mode == "ranked" && match.Status == "completed";
        foreach (var participant in humans)
        {
            var playerId = participant.PlayerId!.Value;
            var won = match.Result == participant.Side + "_win";
            var draw = match.Result == "draw";
            var reward = rewards.Reward(match.Mode, match.Status, match.Result, participant.Side,
                frozenPolicy.WinnerCoinReward, frozenPolicy.DrawCoinReward);
            participant.CoinReward = reward;
            if (reward > 0)
            {
                var wallet = wallets[playerId]; wallet.Balance += reward;
                db.CoinTransactions.Add(new CoinTransaction { Id = Guid.NewGuid(), PlayerId = playerId, IdempotencyKey = $"match:{match.Id}", Kind = "match_reward",
                    Amount = reward, BalanceAfter = wallet.Balance, MatchId = match.Id, MatchSide = participant.Side, CreatedAt = clock.GetUtcNow() });
            }
            if (!isRankedResult) continue;
            var own = ratings[playerId];
            var other = humans.Single(x => x.PlayerId != playerId);
            var otherElo = startingElo[other.PlayerId!.Value];
            var score = won ? 1d : draw ? .5d : 0d;
            participant.EloBefore = startingElo[playerId];
            own.Elo = ratingPolicy.Calculate(startingElo[playerId], otherElo, score, frozenPolicy.EloKFactor);
            participant.EloAfter = own.Elo;
            own.GamesPlayed++; if (won) own.Wins++; else if (draw) own.Draws++; else own.Losses++;
        }
        match.SettledAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var human in humans) matchmaking.Release(human.PlayerId!.Value);
        await hub.BroadcastAsync(matchId, new { type = "match.settled", payload = new { matchId,
            participants = match.Participants.Select(x => new { x.Side, x.CoinReward, x.EloBefore, x.EloAfter }).ToArray() } }, CancellationToken.None);
    }
}

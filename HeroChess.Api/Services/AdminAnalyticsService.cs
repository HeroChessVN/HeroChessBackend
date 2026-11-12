// Vai trò file: Số liệu cân bằng và bot cho admin. Chỉ tính từ dữ liệu trận đã kết thúc và luôn kèm cỡ mẫu.
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class AdminAnalyticsService(AppDbContext db, TimeProvider clock)
{
    // StatsAsync: Số liệu tổng quan cho dashboard; đếm trực tiếp trên bảng, không dùng số liệu dựng sẵn.
    public async Task<AdminStatsDto> StatsAsync(CancellationToken ct)
    {
        var totalPlayers = await db.Players.CountAsync(ct);
        var activePlayers = await db.Players.CountAsync(x => x.Status == "active", ct);
        var totalHeroes = await db.Heroes.CountAsync(ct);
        var enabledHeroes = await db.Heroes.CountAsync(x => x.IsEnabled, ct);
        var inShop = await db.Heroes.CountAsync(x => x.IsInShop && x.IsEnabled, ct);
        var totalMatches = await db.Matches.CountAsync(ct);
        var completed = await db.Matches.CountAsync(x => x.Status == "completed", ct);
        var ranked = await db.Matches.CountAsync(x => x.Mode == "ranked", ct);
        var bot = await db.Matches.CountAsync(x => x.Mode == "bot", ct);
        var active = await db.Matches.CountAsync(x => x.Status == "active", ct);
        var balance = await db.Wallets.SumAsync(x => (long?)x.Balance, ct) ?? 0;
        var admins = await db.Admins.CountAsync(ct);
        return new(totalPlayers, activePlayers, totalPlayers - activePlayers, totalHeroes, enabledHeroes, inShop,
            totalMatches, completed, ranked, bot, active, balance, admins, clock.GetUtcNow());
    }

    // BalanceAsync: Báo cáo cân bằng cho một mode (ranked hoặc bot); cỡ mẫu đi kèm mọi tỉ lệ.
    public async Task<AdminBalanceAnalyticsDto> BalanceAsync(string mode, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var normalized = mode.Trim().ToLowerInvariant();
        if (normalized is not ("ranked" or "bot")) throw new ApiException(400, "INVALID_MODE", "mode must be 'ranked' or 'bot'.");
        var query = db.Matches.AsNoTracking().Where(x => x.Mode == normalized && x.Status == "completed");
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(x => x.CreatedAt <= to.Value);
        var matches = await query.Select(x => new { x.Id, x.CreatedAt, x.StartedAt, x.EndedAt, x.Result, x.RulesetId }).ToListAsync(ct);
        var matchIds = matches.Select(x => x.Id).ToArray();
        var participants = matchIds.Length == 0 ? [] : await db.MatchParticipants.AsNoTracking().Where(x => matchIds.Contains(x.MatchId)).ToListAsync(ct);
        var byMatch = participants.GroupBy(x => x.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        var durations = matches.Where(x => x.StartedAt is not null && x.EndedAt is not null)
            .Select(x => (x.EndedAt!.Value - x.StartedAt!.Value).TotalSeconds).ToList();
        var durationsForHumans = new List<double>();
        var heroStats = new Dictionary<Guid, (int Picks, int Matches, int Wins, int Draws, int Losses)>();
        var factionStats = new Dictionary<string, (int Matches, int Wins, int Draws, int Losses)>();
        var skillUses = new Dictionary<Guid, int>();
        var firstMoveWins = 0;
        var firstMoveSamples = 0;

        foreach (var match in matches)
        {
            var sides = byMatch.GetValueOrDefault(match.Id) ?? [];
            var human = sides.Where(x => x.ParticipantType == "human").ToList();
            if (human.Count == 2)
            {
                durationsForHumans.Add((match.EndedAt!.Value - match.StartedAt!.Value).TotalSeconds);
                // Lợi thế đi trước: phe có hành động đầu tiên và tỉ lệ thắng của phe đi trước.
                var first = await FirstActorAsync(match.Id, ct);
                if (first is not null && human.Any(x => x.Side == first))
                {
                    firstMoveSamples++;
                    if (match.Result == first + "_win") firstMoveWins++;
                }
            }
            foreach (var side in sides)
            {
                var outcome = Outcome(match.Result, side.Side);
                var picked = PickedHeroes(side.LineupSnapshot);
                var pickedFactions = await FactionCodesAsync(picked, ct);
                foreach (var heroId in picked)
                {
                    var current = heroStats.GetValueOrDefault(heroId);
                    heroStats[heroId] = (current.Picks + 1, current.Matches + 1,
                        current.Wins + (outcome == "win" ? 1 : 0), current.Draws + (outcome == "draw" ? 1 : 0),
                        current.Losses + (outcome == "loss" ? 1 : 0));
                }
                foreach (var faction in pickedFactions)
                {
                    var current = factionStats.GetValueOrDefault(faction);
                    factionStats[faction] = (current.Matches + 1,
                        current.Wins + (outcome == "win" ? 1 : 0), current.Draws + (outcome == "draw" ? 1 : 0),
                        current.Losses + (outcome == "loss" ? 1 : 0));
                }
                foreach (var skill in PickedSkills(side.LineupSnapshot))
                    skillUses[skill] = skillUses.GetValueOrDefault(skill) + 1;
            }
        }

        var heroIds = heroStats.Keys.ToArray();
        var heroes = await db.Heroes.AsNoTracking().Where(x => heroIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var links = await db.HeroFactions.AsNoTracking().Where(x => heroIds.Contains(x.HeroId)).ToListAsync(ct);
        var factionIds = links.Select(x => x.FactionId).Distinct().ToArray();
        var factionCodes = await db.Factions.AsNoTracking().Where(x => factionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var factionByHero = links.Where(x => factionCodes.ContainsKey(x.FactionId)).GroupBy(x => x.HeroId)
            .ToDictionary(x => x.Key, x => factionCodes[x.First().FactionId]);
        var playerMatchCount = participants.Count(x => x.ParticipantType == "human");
        var totalPicks = heroStats.Values.Sum(x => x.Picks);

        var heroDtos = heroStats.Select(entry =>
        {
            var hero = heroes.GetValueOrDefault(entry.Key);
            var stats = entry.Value;
            return new AdminHeroWinRateDto(entry.Key, hero?.Code ?? "?", hero?.Name ?? "?",
                factionByHero.GetValueOrDefault(entry.Key), stats.Picks, stats.Matches, stats.Wins, stats.Draws, stats.Losses,
                WinRate(stats.Wins, stats.Matches),
                totalPicks == 0 ? null : Math.Round(100.0 * stats.Picks / totalPicks, 2));
        }).OrderByDescending(x => x.Picks).ThenBy(x => x.HeroCode).ToArray();

        var factionDtos = factionStats.Select(entry => new AdminFactionUsageDto(entry.Key, entry.Value.Matches, entry.Value.Wins,
            entry.Value.Draws, entry.Value.Losses, WinRate(entry.Value.Wins, entry.Value.Matches)))
            .OrderByDescending(x => x.Matches).ToArray();

        var skillIdList = skillUses.Keys.ToArray();
        var skillRows = await db.TeamSkills.AsNoTracking().Where(x => skillIdList.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var skillDtos = skillUses.Select(entry => new AdminSkillUsageDto(entry.Key, skillRows.GetValueOrDefault(entry.Key)?.Code ?? "?",
            skillRows.GetValueOrDefault(entry.Key)?.Name ?? "?", 0, entry.Value)).OrderByDescending(x => x.Uses).ToArray();

        var averageDuration = durationsForHumans.Count == 0 ? (double?)null
            : Math.Round(durationsForHumans.Average(), 1);
        return new(normalized, matches.Count, playerMatchCount,
            firstMoveSamples == 0 ? new(null, 0) : new(Math.Round(100.0 * firstMoveWins / firstMoveSamples, 2), firstMoveSamples),
            averageDuration, heroDtos, factionDtos, skillDtos);
    }

    // BotAnalyticsAsync: Báo cáo bot tách khỏi cân bằng PvP; tỉ lệ tính trên trận bot đã kết thúc.
    public async Task<AdminBotAnalyticsDto> BotAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = db.Matches.AsNoTracking().Where(x => x.Mode == "bot" && x.Status == "completed");
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(x => x.CreatedAt <= to.Value);
        var matches = await query.Select(x => new { x.Id, x.Result, x.RulesetId }).ToListAsync(ct);
        var matchIds = matches.Select(x => x.Id).ToArray();
        var participants = matchIds.Length == 0 ? [] : await db.MatchParticipants.AsNoTracking().Where(x => matchIds.Contains(x.MatchId)).ToListAsync(ct);

        var botWins = 0; var playerWins = 0; var draws = 0;
        var byDifficulty = new Dictionary<string, (int Matches, int Wins, int Draws, int Losses)>();
        var byRuleset = new Dictionary<Guid, (int Matches, int Wins, int Draws, int Losses)>();
        var rulesetCodes = await db.Rulesets.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, ct);

        foreach (var match in matches)
        {
            var sides = participants.Where(x => x.MatchId == match.Id).ToList();
            var bot = sides.FirstOrDefault(x => x.ParticipantType == "bot");
            var human = sides.FirstOrDefault(x => x.ParticipantType == "human");
            if (bot is null || human is null) continue;
            var outcome = Outcome(match.Result, bot.Side);
            if (outcome == "win") botWins++; else if (outcome == "draw") draws++; else playerWins++;
            var difficulty = BotConfiguration.Client(bot.BotConfig)?.Difficulty ?? "unknown";
            var current = byDifficulty.GetValueOrDefault(difficulty);
            byDifficulty[difficulty] = (current.Matches + 1, current.Wins + (outcome == "win" ? 1 : 0),
                current.Draws + (outcome == "draw" ? 1 : 0), current.Losses + (outcome == "loss" ? 1 : 0));
            var ruleset = byRuleset.GetValueOrDefault(match.RulesetId);
            byRuleset[match.RulesetId] = (ruleset.Matches + 1, ruleset.Wins + (outcome == "win" ? 1 : 0),
                ruleset.Draws + (outcome == "draw" ? 1 : 0), ruleset.Losses + (outcome == "loss" ? 1 : 0));
        }

        var total = matches.Count;
        return new(total, botWins, playerWins, draws, WinRate(botWins, total),
            byDifficulty.Select(x => new AdminBotDifficultyDto(x.Key, x.Value.Matches, x.Value.Wins, x.Value.Draws, x.Value.Losses,
                WinRate(x.Value.Wins, x.Value.Matches))).OrderBy(x => x.Difficulty).ToArray(),
            byRuleset.Select(x => new AdminBotRulesetDto(x.Key, rulesetCodes.GetValueOrDefault(x.Key), x.Value.Matches, x.Value.Wins,
                x.Value.Draws, x.Value.Losses, WinRate(x.Value.Wins, x.Value.Matches))).ToArray());
    }

    // FirstActorAsync: Phe làm hành động đầu tiên (sequence khác 0); null nếu chưa có hành động.
    private async Task<string?> FirstActorAsync(Guid matchId, CancellationToken ct) =>
        await db.MatchActions.AsNoTracking().Where(x => x.MatchId == matchId && x.SequenceNo > 0)
            .OrderBy(x => x.SequenceNo).Select(x => x.ActorSide).FirstOrDefaultAsync(ct);

    // FactionCodesAsync: Mã phe của các hero đã chọn; hero không thuộc phe không tính.
    private async Task<IReadOnlyList<string>> FactionCodesAsync(IReadOnlyList<Guid> heroIds, CancellationToken ct)
    {
        if (heroIds.Count == 0) return [];
        var links = await db.HeroFactions.AsNoTracking().Where(x => heroIds.Contains(x.HeroId)).ToListAsync(ct);
        var ids = links.Select(x => x.FactionId).Distinct().ToArray();
        var codes = await db.Factions.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => x.Code).ToListAsync(ct);
        return codes;
    }

    // PickedHeroes: Đọc danh sách heroId từ lineup snapshot đã đóng băng.
    private static IReadOnlyList<Guid> PickedHeroes(JsonDocument snapshot)
    {
        if (snapshot.RootElement.ValueKind != JsonValueKind.Object) return [];
        if (!snapshot.RootElement.TryGetProperty("pieces", out var pieces) || pieces.ValueKind != JsonValueKind.Array) return [];
        return pieces.EnumerateArray().Where(p => p.TryGetProperty("heroId", out _))
            .Select(p => p.GetProperty("heroId").GetGuid()).ToArray();
    }

    // PickedSkills: Đọc danh sách skillId từ lineup snapshot đã đóng băng.
    private static IReadOnlyList<Guid> PickedSkills(JsonDocument snapshot)
    {
        if (snapshot.RootElement.ValueKind != JsonValueKind.Object) return [];
        if (!snapshot.RootElement.TryGetProperty("skills", out var skills) || skills.ValueKind != JsonValueKind.Array) return [];
        return skills.EnumerateArray().Where(s => s.TryGetProperty("skillId", out _))
            .Select(s => s.GetProperty("skillId").GetGuid()).ToArray();
    }

    // WinRate: Tỉ lệ thắng theo phần trăm; null khi không có mẫu để không hiển thị số 0 giả.
    private static double? WinRate(int wins, int samples) => samples == 0 ? null : Math.Round(100.0 * wins / samples, 2);

    // Outcome: Đổi result của trận sang win/loss/draw theo phe.
    private static string? Outcome(string? result, string side) => result switch
    {
        null => null,
        "draw" => "draw",
        _ when result == side + "_win" => "win",
        _ when result.EndsWith("_win", StringComparison.Ordinal) => "loss",
        _ => null
    };
}

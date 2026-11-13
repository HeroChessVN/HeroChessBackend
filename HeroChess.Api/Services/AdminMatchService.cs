// Vai trò file: Tra cứu trận cho admin: danh sách, chi tiết, lineup đã đóng băng và nhật ký hành động. Không nới quyền của player.
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class AdminMatchService(AppDbContext db)
{
    // ListAsync: Danh sách trận với bộ lọc mode/status/khoảng thời gian; phân trang keyset theo thời điểm tạo.
    public async Task<AdminMatchPageDto> ListAsync(string? mode, string? status, DateTimeOffset? from, DateTimeOffset? to,
        Guid? playerId, string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = db.Matches.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(mode)) query = query.Where(x => x.Mode == mode);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(x => x.CreatedAt <= to.Value);
        if (playerId is not null) query = query.Where(x => db.MatchParticipants.Any(p => p.MatchId == x.Id && p.PlayerId == playerId));
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var (at, id) = CursorCodec.Parse(cursor);
            query = query.Where(x => x.CreatedAt < at || (x.CreatedAt == at && x.Id.CompareTo(id) < 0));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var dtos = await BuildMatchesAsync(page, ct);
        string? next = null;
        if (rows.Count > limit) next = CursorCodec.Format(page[^1].CreatedAt, page[^1].Id);
        return new(dtos, next, total);
    }

    // DetailAsync: Chi tiết trận: snapshot luật, lineup đã đóng băng hai phe và cờ có replay.
    public async Task<AdminMatchDetailDto> DetailAsync(Guid matchId, CancellationToken ct)
    {
        var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId, ct)
            ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        var dto = (await BuildMatchesAsync([match], ct))[0];
        var lineups = await BuildLineupsAsync(matchId, ct);
        var actionCount = await db.MatchActions.AsNoTracking().CountAsync(x => x.MatchId == matchId, ct);
        var firstAction = await db.MatchActions.AsNoTracking().Where(x => x.MatchId == matchId)
            .OrderBy(x => x.SequenceNo).Select(x => (DateTimeOffset?)x.CommittedAt).FirstOrDefaultAsync(ct);
        return new(dto, match.RulesetSnapshot.RootElement.Clone(), match.ContentVersion, lineups, actionCount > 0, firstAction);
    }

    // ActionsAsync: Nhật ký hành động theo thứ tự sequence; đây là log đã ghi, không phải replay mô phỏng lại.
    public async Task<IReadOnlyList<AdminMatchActionDto>> ActionsAsync(Guid matchId, int afterSequence, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        return await db.MatchActions.AsNoTracking().Where(x => x.MatchId == matchId && x.SequenceNo > afterSequence)
            .OrderBy(x => x.SequenceNo).Take(limit)
            .Select(x => new AdminMatchActionDto(x.SequenceNo, x.Kind, x.ActorSide,
                x.RequestPayload.RootElement.Clone(), x.ResolvedEvents.RootElement.Clone(), x.CommittedAt)).ToListAsync(ct);
    }

    // BuildMatchesAsync: Dựng DTO trận kèm hai phe, số hành động và số quân mỗi phe.
    private async Task<IReadOnlyList<AdminMatchDto>> BuildMatchesAsync(IReadOnlyList<GameMatch> matches, CancellationToken ct)
    {
        if (matches.Count == 0) return [];
        var ids = matches.Select(x => x.Id).ToArray();
        var participants = await db.MatchParticipants.AsNoTracking().Where(x => ids.Contains(x.MatchId)).ToListAsync(ct);
        var actionCounts = await db.MatchActions.AsNoTracking().Where(x => ids.Contains(x.MatchId)).GroupBy(x => x.MatchId)
            .Select(g => new { MatchId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.MatchId, x => x.Count, ct);
        var rulesets = await db.Rulesets.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var playerIds = participants.Where(x => x.PlayerId is not null).Select(x => x.PlayerId!.Value).Distinct().ToArray();
        var names = await db.Players.AsNoTracking().Where(x => playerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var heroCounts = await HeroCountsAsync(ids, ct);
        return matches.Select(match => new AdminMatchDto(match.Id, match.Mode, match.Status, match.Result, match.EndReason,
            match.RulesetId, rulesets.GetValueOrDefault(match.RulesetId), match.CreatedAt, match.StartedAt, match.EndedAt, match.SettledAt,
            actionCounts.GetValueOrDefault(match.Id),
            participants.Where(p => p.MatchId == match.Id).OrderBy(p => p.Side).Select(p => new AdminMatchSideDto(p.Side, p.PlayerId,
                p.PlayerId is not null ? names.GetValueOrDefault(p.PlayerId.Value) : null, p.ParticipantType, p.ParticipantType == "bot",
                BotConfiguration.Client(p.BotConfig)?.Difficulty, Outcome(match.Result, p.Side), p.EloBefore, p.EloAfter, p.CoinReward,
                heroCounts.GetValueOrDefault((match.Id, p.Side)))).ToArray())).ToArray();
    }

    // BuildLineupsAsync: Đọc lineup snapshot đã đóng băng của từng phe và bổ sung mã hero/skill/phe từ catalog.
    private async Task<IReadOnlyList<AdminMatchSideLineupDto>> BuildLineupsAsync(Guid matchId, CancellationToken ct)
    {
        var snapshots = await db.MatchParticipants.AsNoTracking().Where(x => x.MatchId == matchId)
            .ToDictionaryAsync(x => x.Side, x => x.LineupSnapshot, ct);
        var frozen = snapshots.ToDictionary(x => x.Key, x => Frozen(x.Value));
        var heroIds = frozen.Values.SelectMany(x => x.Pieces).Select(x => x.HeroId).Distinct().ToArray();
        var skillIds = frozen.Values.SelectMany(x => x.Skills).Select(x => x.SkillId).Distinct().ToArray();
        var heroes = await db.Heroes.AsNoTracking().Where(x => heroIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var skills = await db.TeamSkills.AsNoTracking().Where(x => skillIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var links = await db.HeroFactions.AsNoTracking().Where(x => heroIds.Contains(x.HeroId)).ToListAsync(ct);
        var factionIds = links.Select(x => x.FactionId).Distinct().ToArray();
        var factionCodes = await db.Factions.AsNoTracking().Where(x => factionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var factionByHero = links.Where(x => factionCodes.ContainsKey(x.FactionId)).GroupBy(x => x.HeroId)
            .ToDictionary(x => x.Key, x => factionCodes[x.First().FactionId]);
        var result = new List<AdminMatchSideLineupDto>();
        foreach (var side in snapshots.Keys.OrderBy(x => x))
        {
            var lineup = frozen[side];
            result.Add(new AdminMatchSideLineupDto(side,
                lineup.Pieces.OrderBy(x => x.SlotNo).Select(piece => new AdminMatchHeroPickDto(piece.SlotNo, piece.HeroId,
                    heroes.GetValueOrDefault(piece.HeroId)?.Code, piece.HeroName ?? heroes.GetValueOrDefault(piece.HeroId)?.Name,
                    piece.ClassCode, piece.SetupPoints, factionByHero.GetValueOrDefault(piece.HeroId),
                    piece.StartX, piece.StartY)).ToArray(),
                lineup.Skills.OrderBy(x => x.SlotNo).Select(skill => new AdminMatchSkillPickDto(skill.SlotNo, skill.SkillId,
                    skills.GetValueOrDefault(skill.SkillId)?.Code, skill.Name ?? skills.GetValueOrDefault(skill.SkillId)?.Name)).ToArray()));
        }
        return result;
    }

    // HeroCountsAsync: Đếm số quân theo phe từ lineup snapshot (mảng pieces).
    private async Task<Dictionary<(Guid, string), int>> HeroCountsAsync(Guid[] matchIds, CancellationToken ct)
    {
        var rows = await db.MatchParticipants.AsNoTracking().Where(x => matchIds.Contains(x.MatchId)).ToListAsync(ct);
        var counts = new Dictionary<(Guid, string), int>();
        foreach (var row in rows)
        {
            if (row.LineupSnapshot.RootElement.ValueKind != JsonValueKind.Object) continue;
            if (!row.LineupSnapshot.RootElement.TryGetProperty("pieces", out var pieces) || pieces.ValueKind != JsonValueKind.Array) continue;
            counts[(row.MatchId, row.Side)] = pieces.GetArrayLength();
        }
        return counts;
    }

    // Frozen: Đọc snapshot lineup; snapshot rỗng (trận chưa chọn đội hình) trả về lineup rỗng.
    private static FrozenLineup Frozen(JsonDocument snapshot) =>
        snapshot.RootElement.ValueKind == JsonValueKind.Object && snapshot.RootElement.TryGetProperty("pieces", out _)
            ? GameJson.Read<FrozenLineup>(snapshot)
            : new FrozenLineup(Guid.Empty, 0, 0, [], []);

    // Outcome: Đổi result của trận sang win/loss/draw theo phe; không suy đoán khi result rỗng.
    private static string? Outcome(string? result, string side) => result switch
    {
        null => null,
        "draw" => "draw",
        _ when result == side + "_win" => "win",
        _ when result.EndsWith("_win", StringComparison.Ordinal) => "loss",
        _ => null
    };
}

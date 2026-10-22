// Vai trò file: Đọc lịch sử theo chủ trận, chi tiết trận và replay từ stateAfter đã lưu, không mô phỏng lại bằng catalog hiện tại.
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using HeroChess.Rules;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class MatchHistoryService(AppDbContext db)
{
    // ListAsync: Phân trang keyset theo thời điểm/ID; chỉ liệt kê trận actor tham gia. Lọc theo mode và kết quả nếu có.
    public async Task<MatchPageDto> ListAsync(Guid playerId, string? cursor, int limit, string? mode, string? outcome, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 50);
        var query = from p in db.MatchParticipants.AsNoTracking()
                    join m in db.Matches.AsNoTracking() on p.MatchId equals m.Id
                    where p.PlayerId == playerId
                    select new { Match = m, p.Side };
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var (at, id) = CursorCodec.Parse(cursor);
            query = query.Where(x => x.Match.CreatedAt < at || (x.Match.CreatedAt == at && x.Match.Id.CompareTo(id) < 0));
        }
        if (!string.IsNullOrWhiteSpace(mode)) query = query.Where(x => x.Match.Mode == mode);
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            var wanted = outcome.Trim().ToLowerInvariant();
            query = wanted switch
            {
                "win" => query.Where(x => x.Match.Result != null && x.Match.Result == x.Side + "_win"),
                "draw" => query.Where(x => x.Match.Result == "draw"),
                "loss" => query.Where(x => x.Match.Result != null && x.Match.Result != "draw" && x.Match.Result != x.Side + "_win" && x.Match.Result.EndsWith("_win")),
                _ => query
            };
        }
        var rows = await query.OrderByDescending(x => x.Match.CreatedAt).ThenByDescending(x => x.Match.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToArray();
        var sides = await SidesOfAsync(page.Select(x => x.Match.Id), ct);
        var items = page.Select(x =>
        {
            var mine = sides.First(s => s.MatchId == x.Match.Id && s.PlayerId == playerId);
            var opponent = sides.FirstOrDefault(s => s.MatchId == x.Match.Id && s.PlayerId != playerId);
            return new MatchListItemDto(x.Match.Id, x.Match.Mode, x.Match.Status, x.Side, x.Match.Result, x.Match.EndReason,
                x.Match.CreatedAt, x.Match.EndedAt, Outcome(x.Match.Result, x.Side),
                opponent?.PlayerId, opponent?.DisplayName,
                mine.EloBefore, mine.EloAfter,
                mine.EloBefore is not null && mine.EloAfter is not null ? mine.EloAfter - mine.EloBefore : null,
                Duration(x.Match.StartedAt, x.Match.EndedAt),
                sides.Any(s => s.MatchId == x.Match.Id && s.ReplayAvailable));
        }).ToArray();
        string? next = null;
        if (rows.Count > limit && page.Length > 0)
        {
            var last = page[^1].Match;
            next = CursorCodec.Format(last.CreatedAt, last.Id);
        }
        return new(items, next);
    }

    // DetailAsync: Chi tiết trận cho participant; đối thủ, Elo, thời lượng và lineup đã đóng băng.
    public async Task<MatchDetailDto> DetailAsync(Guid playerId, Guid matchId, CancellationToken ct)
    {
        var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId, ct)
            ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        var sides = await SidesOfAsync([matchId], ct);
        if (!sides.Any(x => x.PlayerId == playerId)) throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        var actionCount = await db.MatchActions.AsNoTracking().CountAsync(x => x.MatchId == matchId, ct);
        var lineups = await LineupOfAsync(matchId, sides, ct);
        var dtoSides = sides.OrderBy(x => x.Side).Select(side => new MatchSideDto(side.Side, side.PlayerId, side.DisplayName, side.ParticipantType,
            Outcome(match.Result, side.Side), side.EloBefore, side.EloAfter, side.CoinReward, lineups[side.Side])).ToArray();
        return new(match.Id, match.Mode, match.Status, match.Result, match.EndReason, match.CreatedAt, match.StartedAt, match.EndedAt,
            dtoSides, actionCount > 0, actionCount);
    }

    // LineupAsync: Lineup đã dùng trong trận; bên đối thủ chỉ hiện khi trận đã bắt đầu.
    public async Task<MatchLineupDto> LineupAsync(Guid playerId, Guid matchId, CancellationToken ct)
    {
        var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId, ct)
            ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        var sides = await SidesOfAsync([matchId], ct);
        if (!sides.Any(x => x.PlayerId == playerId)) throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        var lineups = await LineupOfAsync(matchId, sides, ct);
        var visible = sides.Where(x => x.PlayerId == playerId || match.Status != "selecting")
            .OrderBy(x => x.Side).Select(x => new MatchSideLineupDto(x.Side, lineups[x.Side].Entries, lineups[x.Side].Skills)).ToArray();
        return new(matchId, visible);
    }

    // ReplayAsync: Participant xem được action sau khi trận bắt đầu; đọc theo sequence, trả cursor cho trang tiếp.
    public async Task<ReplayPageDto> ReplayAsync(Guid playerId, Guid matchId, int afterSequence, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId, ct);
        var participant = await db.MatchParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId && x.PlayerId == playerId, ct);
        if (match is null || participant is null)
            throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        if (match.Status is not ("active" or "completed" or "cancelled")) throw new ApiException(409, "REPLAY_NOT_AVAILABLE", "Actions are available after the match starts.");
        var rows = await db.MatchActions.AsNoTracking().Where(x => x.MatchId == matchId && x.SequenceNo > afterSequence).OrderBy(x => x.SequenceNo).Take(limit + 1).ToListAsync(ct);
        var viewer = participant.Side == "red" ? Side.Red : Side.Black;
        var entries = rows.Take(limit).Select(x => new ReplayEntryDto(x.SequenceNo, x.Kind == "hero_active" ? "legacy_action" : x.Kind, x.ActorSide,
            MatchStateProjection.Events(x.ResolvedEvents.RootElement, viewer), MatchStateProjection.State(x.StateAfter.RootElement, viewer), x.CommittedAt)).ToArray();
        return new(matchId, entries, rows.Count > limit ? entries[^1].SequenceNo : null);
    }

    // SideRow: Một bên của trận gộp participant, tên hiển thị và cờ replay có dữ liệu.
    private sealed record SideRow(Guid MatchId, string Side, Guid? PlayerId, string? DisplayName, string ParticipantType,
        int? EloBefore, int? EloAfter, long CoinReward, bool ReplayAvailable);

    // SidesOfAsync: Nạp participant của các trận kèm tên hiển thị; cờ replay dựa trên việc trận đã có action lưu.
    private async Task<List<SideRow>> SidesOfAsync(IEnumerable<Guid> matchIds, CancellationToken ct)
    {
        var ids = matchIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var replayed = await db.MatchActions.AsNoTracking().Where(x => ids.Contains(x.MatchId)).Select(x => x.MatchId).Distinct().ToListAsync(ct);
        var participants = await db.MatchParticipants.AsNoTracking().Where(x => ids.Contains(x.MatchId)).ToListAsync(ct);
        var playerIds = participants.Where(x => x.PlayerId is not null).Select(x => x.PlayerId!.Value).Distinct().ToArray();
        var names = await db.Players.AsNoTracking().Where(x => playerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var replaySet = replayed.ToHashSet();
        return participants.Select(x => new SideRow(x.MatchId, x.Side, x.PlayerId,
            x.PlayerId is not null && names.TryGetValue(x.PlayerId.Value, out var name) ? name : null,
            x.ParticipantType, x.EloBefore, x.EloAfter, x.CoinReward, replaySet.Contains(x.MatchId))).ToList();
    }

    // LineupOfAsync: Đọc lineup snapshot của từng bên rồi bổ sung mã hero và phe từ catalog hiện tại.
    private async Task<Dictionary<string, MatchSideLineupDto>> LineupOfAsync(Guid matchId, List<SideRow> sides, CancellationToken ct)
    {
        var snapshots = await db.MatchParticipants.AsNoTracking().Where(x => x.MatchId == matchId)
            .ToDictionaryAsync(x => x.Side, x => x.LineupSnapshot, ct);
        var frozen = snapshots.ToDictionary(x => x.Key, x => Frozen(x.Value));
        var heroIds = frozen.Values.SelectMany(x => x.Pieces).Select(x => x.HeroId).Distinct().ToArray();
        var skillIds = frozen.Values.SelectMany(x => x.Skills).Select(x => x.SkillId).Distinct().ToArray();
        var heroes = await db.Heroes.AsNoTracking().Where(x => heroIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var skills = await db.TeamSkills.AsNoTracking().Where(x => skillIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var factionLinks = await db.HeroFactions.AsNoTracking().Where(x => heroIds.Contains(x.HeroId)).ToListAsync(ct);
        var factionIds = factionLinks.Select(x => x.FactionId).Distinct().ToArray();
        var factionCodes = await db.Factions.AsNoTracking().Where(x => factionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var factionByHero = factionLinks
            .Where(x => factionCodes.ContainsKey(x.FactionId))
            .GroupBy(x => x.HeroId)
            .ToDictionary(x => x.Key, x => factionCodes[x.First().FactionId]);
        var result = new Dictionary<string, MatchSideLineupDto>(StringComparer.Ordinal);
        foreach (var side in sides)
        {
            if (!frozen.TryGetValue(side.Side, out var lineup)) { result[side.Side] = new(side.Side, [], []); continue; }
            var entries = lineup.Pieces.OrderBy(x => x.SlotNo).Select(piece => new MatchLineupEntryDto(piece.SlotNo, piece.HeroId,
                heroes.TryGetValue(piece.HeroId, out var hero) ? hero.Code : null,
                piece.HeroName ?? (heroes.TryGetValue(piece.HeroId, out var named) ? named.Name : null),
                piece.ClassCode, piece.SetupPoints, factionByHero.GetValueOrDefault(piece.HeroId),
                piece.StartX, piece.StartY, piece.CosmeticId)).ToArray();
            var skillDtos = lineup.Skills.OrderBy(x => x.SlotNo).Select(x => new MatchLineupSkillDto(x.SlotNo, x.SkillId,
                skills.TryGetValue(x.SkillId, out var skill) ? skill.Code : null,
                x.Name ?? (skills.TryGetValue(x.SkillId, out var named) ? named.Name : null), x.ImplementationKey)).ToArray();
            result[side.Side] = new(side.Side, entries, skillDtos);
        }
        return result;
    }

    // Frozen: Đọc snapshot lineup; snapshot rỗng (trận chưa chọn đội hình) trả về lineup rỗng.
    private static FrozenLineup Frozen(JsonDocument snapshot) =>
        snapshot.RootElement.ValueKind == JsonValueKind.Object && snapshot.RootElement.TryGetProperty("pieces", out _)
            ? GameJson.Read<FrozenLineup>(snapshot)
            : new FrozenLineup(Guid.Empty, 0, 0, [], []);

    // Outcome: Quy đổi result của trận sang win/loss/draw theo phe; trận chưa có result thì null, không suy đoán.
    private static string? Outcome(string? result, string side) => result switch
    {
        null => null,
        "draw" => "draw",
        _ when result == side + "_win" => "win",
        _ when result.EndsWith("_win", StringComparison.Ordinal) => "loss",
        _ => null
    };

    // Duration: Thời lượng trận tính bằng giây từ lúc bắt đầu đến lúc kết thúc.
    private static double? Duration(DateTimeOffset? startedAt, DateTimeOffset? endedAt) =>
        startedAt is null || endedAt is null ? null : Math.Round((endedAt.Value - startedAt.Value).TotalSeconds, 1);
}

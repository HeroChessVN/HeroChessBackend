// Vai trò file: Đọc lịch sử theo chủ trận và replay từ stateAfter đã lưu, không mô phỏng lại bằng catalog hiện tại.
using System.Text;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;
using HeroChess.Rules;

namespace HeroChess.Api.Services;

public sealed class MatchHistoryService(AppDbContext db)
{
    // ListAsync: Phân trang keyset theo thời điểm/ID; chỉ liệt kê trận actor tham gia.
    public async Task<MatchPageDto> ListAsync(Guid playerId, string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 50);
        var query = from p in db.MatchParticipants.AsNoTracking()
                    join m in db.Matches.AsNoTracking() on p.MatchId equals m.Id
                    where p.PlayerId == playerId
                    select new { Match = m, p.Side };
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var id)) throw new ApiException(400, "INVALID_CURSOR", "The cursor is invalid.");
            var at = new DateTimeOffset(ticks, TimeSpan.Zero);
            query = query.Where(x => x.Match.CreatedAt < at || (x.Match.CreatedAt == at && x.Match.Id.CompareTo(id) < 0));
        }
        var rows = await query.OrderByDescending(x => x.Match.CreatedAt).ThenByDescending(x => x.Match.Id).Take(limit + 1).ToListAsync(ct);
        var items = rows.Take(limit).Select(x => new MatchListItemDto(x.Match.Id, x.Match.Mode, x.Match.Status, x.Side, x.Match.Result, x.Match.EndReason, x.Match.CreatedAt, x.Match.EndedAt)).ToArray();
        string? next = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1].Match;
            next = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.CreatedAt.UtcTicks}|{last.Id}"));
        }
        return new(items, next);
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
        var entries = rows.Take(limit).Select(x => new ReplayEntryDto(x.SequenceNo, x.Kind, x.ActorSide,
            MatchStateProjection.Events(x.ResolvedEvents.RootElement, viewer), MatchStateProjection.State(x.StateAfter.RootElement, viewer), x.CommittedAt)).ToArray();
        return new(matchId, entries, rows.Count > limit ? entries[^1].SequenceNo : null);
    }
}

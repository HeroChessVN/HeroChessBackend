// Vai trò file: Bảng xếp hạng đọc từ player_rating; chỉ trả dữ liệu công khai, không lộ email hay thông tin nhạy cảm.
using HeroChess.Api.Data;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class RankingService(AppDbContext db)
{
    // LeaderboardAsync: Xếp theo Elo giảm dần, tie-break playerId tăng dần; phân trang keyset (elo, playerId) và tính rank tuyệt đối.
    public async Task<RankingPageDto> LeaderboardAsync(int limit, string? cursor, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        // Lọc và sắp xếp trên cột thật rồi mới chiếu ra DTO: EF không dịch được CompareTo trên kiểu chiếu.
        var query = from rating in db.Ratings.AsNoTracking()
                    join player in db.Players.AsNoTracking() on rating.PlayerId equals player.Id
                    select new { rating.PlayerId, player.DisplayName, rating.Elo, rating.GamesPlayed, rating.Wins, rating.Draws, rating.Losses };
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var (elo, id) = CursorCodec.ParseRating(cursor);
            query = query.Where(x => x.Elo < elo || (x.Elo == elo && x.PlayerId.CompareTo(id) > 0));
        }
        var rows = await query.OrderByDescending(x => x.Elo).ThenBy(x => x.PlayerId).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToArray();
        string? next = rows.Count > limit && page.Length > 0 ? CursorCodec.FormatRating(page[^1].Elo, page[^1].PlayerId) : null;
        var total = await db.Ratings.AsNoTracking().CountAsync(ct);
        var items = new List<RankingEntryDto>(page.Length);
        // Hạng tuyệt đối của dòng đầu; các dòng sau tăng dần vì tie-break theo playerId là duy nhất.
        var rank = page.Length == 0 ? 0 : await RankOfAsync(page[0].Elo, page[0].PlayerId, ct);
        foreach (var row in page)
            items.Add(new RankingEntryDto(rank++, row.PlayerId, row.DisplayName, row.Elo, row.GamesPlayed, row.Wins, row.Draws, row.Losses));
        return new(items, next, total);
    }

    // MeAsync: Hạng và rating của người chơi hiện tại; không có hồ sơ rating thì hạng null, không giả lập số liệu.
    public async Task<MyRankingDto> MeAsync(Guid playerId, CancellationToken ct)
    {
        var total = await db.Ratings.AsNoTracking().CountAsync(ct);
        var rating = await db.Ratings.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == playerId, ct);
        var player = await db.Players.AsNoTracking().SingleOrDefaultAsync(x => x.Id == playerId, ct);
        if (rating is null || player is null) return new(null, playerId, player?.DisplayName ?? "", 0, 0, 0, 0, 0, total);
        var rank = await RankOfAsync(rating.Elo, rating.PlayerId, ct);
        return new(rank, rating.PlayerId, player.DisplayName, rating.Elo,
            rating.GamesPlayed, rating.Wins, rating.Draws, rating.Losses, total);
    }

    // RankOfAsync: Hạng tuyệt đối của một dòng = số dòng đứng trước nó theo cùng thứ tự Elo giảm dần, playerId tăng dần.
    private async Task<int> RankOfAsync(int elo, Guid playerId, CancellationToken ct)
    {
        var better = await db.Ratings.AsNoTracking().CountAsync(x =>
            x.Elo > elo || (x.Elo == elo && x.PlayerId.CompareTo(playerId) < 0), ct);
        return better + 1;
    }
}

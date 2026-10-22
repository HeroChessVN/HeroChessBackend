// Vai trò file: Probe tạm: in SQL sinh ra cho Guid.CompareTo và ORDER BY để so ordering .NET với uuid của Postgres.
using HeroChess.Api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class RankingQueryTranslationTests
{
    private static AppDbContext Context()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=hero_chess;Username=hero_chess;Password=none")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Dump_sql()
    {
        using var db = Context();
        var id = Guid.NewGuid();
        var lines = new List<string>();
        lines.Add("== CompareTo ==");
        lines.Add(db.Ratings.Where(x => x.PlayerId.CompareTo(id) < 0).ToQueryString());
        lines.Add("== OrderBy PlayerId ==");
        lines.Add(db.Ratings.OrderBy(x => x.PlayerId).ToQueryString());
        lines.Add("== OrderBy Elo desc, PlayerId ==");
        lines.Add(db.Ratings.OrderByDescending(x => x.Elo).ThenBy(x => x.PlayerId).ToQueryString());
        lines.Add("== Ranking keyset page ==");
        lines.Add((from rating in db.Ratings.AsNoTracking()
            join player in db.Players.AsNoTracking() on rating.PlayerId equals player.Id
            where rating.Elo < 1000 || (rating.Elo == 1000 && rating.PlayerId.CompareTo(id) > 0)
            orderby rating.Elo descending, rating.PlayerId
            select new { rating.PlayerId, player.DisplayName, rating.Elo, rating.GamesPlayed, rating.Wins, rating.Draws, rating.Losses }).Take(51).ToQueryString());
        lines.Add("== Rank count ==");
        lines.Add(db.Ratings.Where(x => x.Elo > 1000 || (x.Elo == 1000 && x.PlayerId.CompareTo(id) < 0)).ToQueryString());
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "hero_probe_sql.txt"), lines);
        Assert.True(File.Exists(Path.Combine(Path.GetTempPath(), "hero_probe_sql.txt")));
    }
}

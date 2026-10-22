// Vai trò file: Test cho bảng xếp hạng, lịch sử trận, chi tiết trận và lineup đã đóng băng.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Contracts;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class RankingAndHistoryTests : IClassFixture<HeroChessFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HeroChessFactory _factory;

    public RankingAndHistoryTests(HeroChessFactory factory) => _factory = factory;

    [Fact]
    // Ranking_orders_by_elo_and_exposes_own_rank: Xếp hạng theo Elo, tie-break theo ID và trả hạng của chính người chơi.
    public async Task Ranking_orders_by_elo_and_exposes_own_rank()
    {
        var first = await CreatePlayer();
        var second = await CreatePlayer();
        // Bảng xếp hạng dùng chung database test nên phải duyệt hết trang mới thấy player vừa tạo.
        var board = await AllRankingRows(first.Client);
        Assert.True(board.Total >= 2);
        Assert.Equal(board.Total, board.Items.Count);
        var ratings = board.Items.Select(x => x.Rating).ToArray();
        Assert.Equal(ratings.OrderByDescending(x => x).ToArray(), ratings);
        // Hạng là vị trí tuyệt đối trong bảng xếp hạng, tăng dần từ 1.
        Assert.Equal(Enumerable.Range(1, board.Items.Count), board.Items.Select(x => x.Rank));

        var mine = await first.Client.GetFromJsonAsync<MyRankingDto>("/api/v1/ranking/me", Json);
        Assert.NotNull(mine);
        Assert.Equal(first.PlayerId, mine!.PlayerId);
        var row = board.Items.Single(x => x.PlayerId == first.PlayerId);
        Assert.Equal(row.Rank, mine.Rank);
        Assert.Equal(row.Rating, mine.Rating);
        Assert.Contains(board.Items, x => x.PlayerId == second.PlayerId);
        // Bảng xếp hạng công khai không lộ email hay tài khoản khác.
        Assert.DoesNotContain("@", System.Text.Json.JsonSerializer.Serialize(board));
    }

    [Fact]
    // Ranking_requires_login: Bảng xếp hạng và hạng cá nhân yêu cầu token hợp lệ.
    public async Task Ranking_requires_login()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/ranking")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/ranking/me")).StatusCode);
    }

    [Fact]
    // Ranking_cursor_pages_without_duplicates: Phân trang cursor không lặp hoặc bỏ sót dòng.
    public async Task Ranking_cursor_pages_without_duplicates()
    {
        var client = (await CreatePlayer()).Client;
        await CreatePlayer(); await CreatePlayer(); await CreatePlayer();
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var path = cursor is null ? "/api/v1/ranking?limit=2" : $"/api/v1/ranking?limit=2&cursor={Uri.EscapeDataString(cursor)}";
            var page = await client.GetFromJsonAsync<RankingPageDto>(path, Json);
            Assert.NotNull(page);
            seen.AddRange(page!.Items.Select(x => x.PlayerId));
            cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(seen.Count, seen.OrderBy(id => id).ToArray().Length);
    }

    [Fact]
    // Match_history_is_private_and_detail_requires_participant: Người ngoài không xem được lịch sử hay chi tiết trận của người khác.
    public async Task Match_history_is_private_and_detail_requires_participant()
    {
        var player = await CreatePlayerWithLineup();
        var observer = await CreatePlayer();
        var ticket = await Post<MatchmakingTicketDto>(player.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        var matchId = ticket.MatchId!.Value;
        await Post<MatchSelectionDto>(player.Client, $"/api/v1/matches/{matchId}/selection",
            new SelectLineupRequest(player.Lineup.Id, player.Lineup.Revision), HttpMethod.Put);
        await player.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null);

        var history = await player.Client.GetFromJsonAsync<MatchPageDto>("/api/v1/matches?limit=20", Json);
        Assert.Contains(history!.Items, x => x.MatchId == matchId);
        Assert.Equal(HttpStatusCode.NotFound, (await observer.Client.GetAsync($"/api/v1/matches/{matchId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await observer.Client.GetAsync($"/api/v1/matches/{matchId}/lineup")).StatusCode);
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/matches?limit=20")).StatusCode);
    }

    [Fact]
    // Match_detail_and_lineup_use_the_frozen_lineup: Chi tiết trận và lineup trả về đội hình đã đóng băng lúc trận, không phải lineup hiện tại.
    public async Task Match_detail_and_lineup_use_the_frozen_lineup()
    {
        var player = await CreatePlayerWithLineup();
        var ticket = await Post<MatchmakingTicketDto>(player.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        var matchId = ticket.MatchId!.Value;
        await Post<MatchSelectionDto>(player.Client, $"/api/v1/matches/{matchId}/selection",
            new SelectLineupRequest(player.Lineup.Id, player.Lineup.Revision), HttpMethod.Put);
        await player.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null);

        var detail = await player.Client.GetFromJsonAsync<MatchDetailDto>($"/api/v1/matches/{matchId}", Json);
        Assert.NotNull(detail);
        Assert.Equal(matchId, detail!.MatchId);
        Assert.Equal(2, detail.Sides.Count);
        var mine = detail.Sides.Single(x => x.PlayerId == player.PlayerId);
        Assert.Equal(player.Lineup.Entries.Count, mine.Lineup.Entries.Count);
        Assert.Equal(player.Lineup.Entries.OrderBy(x => x.SlotNo).Select(x => x.HeroId), mine.Lineup.Entries.OrderBy(x => x.SlotNo).Select(x => x.HeroId));
        Assert.All(mine.Lineup.Entries, entry => Assert.NotNull(entry.ClassCode));

        var lineup = await player.Client.GetFromJsonAsync<MatchLineupDto>($"/api/v1/matches/{matchId}/lineup", Json);
        Assert.NotNull(lineup);
        // Trận đã bắt đầu nên cả hai bên đều hiện lineup đã đóng băng.
        Assert.Equal(2, lineup!.Sides.Count);
        var mySide = lineup.Sides.Single(x => x.Side == detail.Sides.Single(s => s.PlayerId == player.PlayerId).Side);
        Assert.Equal(player.Lineup.Entries.Count, mySide.Entries.Count);
        Assert.Equal(player.Lineup.Entries.OrderBy(x => x.SlotNo).Select(x => x.HeroId), mySide.Entries.OrderBy(x => x.SlotNo).Select(x => x.HeroId));
    }

    [Fact]
    // Match_history_filters_by_mode_and_outcome: Bộ lọc mode và kết quả chỉ trả về trận thuộc bộ lọc.
    public async Task Match_history_filters_by_mode_and_outcome()
    {
        var player = await CreatePlayerWithLineup();
        var ticket = await Post<MatchmakingTicketDto>(player.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        var matchId = ticket.MatchId!.Value;
        await Post<MatchSelectionDto>(player.Client, $"/api/v1/matches/{matchId}/selection",
            new SelectLineupRequest(player.Lineup.Id, player.Lineup.Revision), HttpMethod.Put);
        await player.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null);

        var botMatches = await player.Client.GetFromJsonAsync<MatchPageDto>("/api/v1/matches?mode=bot&limit=20", Json);
        Assert.All(botMatches!.Items, x => Assert.Equal("bot", x.Mode));
        var rankedMatches = await player.Client.GetFromJsonAsync<MatchPageDto>("/api/v1/matches?mode=ranked&limit=20", Json);
        Assert.DoesNotContain(rankedMatches!.Items, x => x.MatchId == matchId);
        var wins = await player.Client.GetFromJsonAsync<MatchPageDto>("/api/v1/matches?outcome=win&limit=20", Json);
        Assert.All(wins!.Items, x => Assert.Equal("win", x.Outcome));
    }

    // CreatePlayer: Đăng ký player mới không cần đội hình; trả về client đã đăng nhập và id player.
    private async Task<(HttpClient Client, Guid PlayerId)> CreatePlayer()
    {
        var client = _factory.CreateClient(); var email = $"rank-{Guid.NewGuid():N}@hero.local"; var password = $"A1!{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password })).StatusCode);
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false", new { email, password })).Content.ReadFromJsonAsync<TokenResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        var me = await client.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        return (client, me!.PlayerId!.Value);
    }

    // CreatePlayerWithLineup: Đăng ký player và tạo đội hình hợp lệ để có thể vào trận.
    private async Task<(HttpClient Client, Guid PlayerId, LineupDto Lineup)> CreatePlayerWithLineup()
    {
        var (client, playerId) = await CreatePlayer();
        var catalog = await client.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json);
        var byClass = catalog!.Heroes.Where(x => x.IsOwned).GroupBy(x => x.ClassCode)
            .ToDictionary(x => x.Key, x => new Queue<HeroDto>(x.OrderBy(h => h.Code)));
        var entries = catalog.Slots.OrderBy(x => x.SlotNo).Select(x => new LineupEntryInput(x.SlotNo, byClass[x.ClassCode].Dequeue().Id, null)).ToArray();
        var skills = catalog.TeamSkills.Take(3).Select((x, i) => new LineupSkillInput(i + 1, x.Id)).ToArray();
        var lineup = await Post<LineupDto>(client, "/api/v1/lineups", new SaveLineupRequest("History fixture", catalog.Ruleset.Id, entries, skills));
        return (client, playerId, lineup);
    }

    // AllRankingRows: Duyệt hết trang bằng cursor để lấy toàn bộ bảng xếp hạng, không phụ thuộc thứ tự tạo player.
    private static async Task<RankingPageDto> AllRankingRows(HttpClient client)
    {
        var items = new List<RankingEntryDto>();
        RankingPageDto? page = null;
        string? cursor = null;
        do
        {
            var path = cursor is null ? "/api/v1/ranking?limit=100" : $"/api/v1/ranking?limit=100&cursor={Uri.EscapeDataString(cursor)}";
            page = await client.GetFromJsonAsync<RankingPageDto>(path, Json);
            Assert.NotNull(page);
            items.AddRange(page!.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return new(items, null, page!.Total);
    }

    // Post: Gửi request JSON, assert thành công rồi deserialize response kiểu T.
    private static async Task<T> Post<T>(HttpClient client, string path, object body, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Json) };
        var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);
}

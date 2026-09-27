// Vai trò file: Phần chính của partial class MatchFlowTests; các file regression bổ sung test vào cùng class, chia sẻ helper và fixture.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HeroChess.Contracts;
using HeroChess.Api.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class MatchFlowTests : IClassFixture<HeroChessFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HeroChessFactory _factory;
    // MatchFlowTests: Nhận factory để tạo request scope và các client test.
    public MatchFlowTests(HeroChessFactory factory) => _factory = factory;

    [Fact]
    // Ranked_match_serializes_competing_commands_broadcasts_and_replays: Kiểm tra hai command cạnh tranh, phát socket, log và replay của ranked.
    public async Task Ranked_match_serializes_competing_commands_broadcasts_and_replays()
    {
        var red = await CreatePlayerWithLineup();
        var black = await CreatePlayerWithLineup();
        var redTicket = await Post<MatchmakingTicketDto>(red.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        Assert.Equal("waiting", redTicket.Status);
        var blackTicket = await Post<MatchmakingTicketDto>(black.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        Assert.Equal("matched", blackTicket.Status);
        var matchId = blackTicket.MatchId!.Value;
        redTicket = await red.Client.GetFromJsonAsync<MatchmakingTicketDto>($"/api/v1/matchmaking/tickets/{redTicket.TicketId}", Json);
        Assert.Equal(matchId, redTicket!.MatchId);

        await Select(red, matchId); await Select(black, matchId);
        var beforeReveal = await red.Client.GetFromJsonAsync<MatchSelectionDto>($"/api/v1/matches/{matchId}/selection", Json);
        Assert.All(beforeReveal!.Sides, side => Assert.Equal(3, side.PublicSkillIds.Count));
        Assert.Null(beforeReveal.Sides.Single(x => x.Side == "black").OwnLineupId);
        var confirmations = await Task.WhenAll(red.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null), black.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null));
        Assert.All(confirmations, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        Assert.Equal(HttpStatusCode.NoContent, (await red.Client.DeleteAsync($"/api/v1/lineups/{red.Lineup.Id}?expectedRevision={red.Lineup.Revision}")).StatusCode);
        var observer = await CreatePlayerWithLineup();
        Assert.Equal(HttpStatusCode.NotFound, (await observer.Client.GetAsync($"/api/v1/matches/{matchId}/state")).StatusCode);

        using var redSocket = await Connect(red.Client);
        using var blackSocket = await Connect(black.Client);
        await Subscribe(redSocket, matchId); await Subscribe(blackSocket, matchId);
        Assert.Equal("match.state", (await Receive(redSocket)).GetProperty("type").GetString());
        Assert.Equal("match.state", (await Receive(blackSocket)).GetProperty("type").GetString());

        var state = await red.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json);
        var legal = await red.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{matchId}/legal-actions", Json);
        Assert.True(legal!.Length > 1);
        var wrongTurn = await black.Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/commands", Command(state!.Version, legal[0]), Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongTurn.StatusCode);
        var rankedUndo = new MatchCommandRequest(Guid.NewGuid(), state.Version, JsonSerializer.SerializeToElement(new { type = "undo", targetSequence = 0 }, Json));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await red.Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/commands", rankedUndo, Json)).StatusCode);
        var first = Command(state.Version, legal[0]); var second = Command(state.Version, legal[1]);
        var attempts = await Task.WhenAll(red.Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/commands", first, Json), red.Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/commands", second, Json));
        Assert.Single(attempts, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(attempts, x => x.StatusCode == HttpStatusCode.Conflict || x.StatusCode == HttpStatusCode.UnprocessableEntity);
        var acceptedIndex = Array.FindIndex(attempts, x => x.StatusCode == HttpStatusCode.OK);
        var acceptedRequest = acceptedIndex == 0 ? first : second;
        Assert.Equal("match.command_accepted", (await Receive(redSocket)).GetProperty("type").GetString());
        Assert.Equal("match.command_accepted", (await Receive(blackSocket)).GetProperty("type").GetString());

        var retry = await Post<MatchCommandResultDto>(red.Client, $"/api/v1/matches/{matchId}/commands", acceptedRequest);
        Assert.True(retry.Duplicate);
        Assert.Equal(1, retry.SequenceNo);
        var conflictingRetry = acceptedRequest with { Action = JsonSerializer.SerializeToElement(new { type = "resign" }, Json) };
        Assert.Equal(HttpStatusCode.Conflict, (await red.Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/commands", conflictingRetry, Json)).StatusCode);
        var current = await black.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json);
        var resign = new MatchCommandRequest(Guid.NewGuid(), current!.Version, JsonSerializer.SerializeToElement(new { type = "resign" }, Json));
        await Post<MatchCommandResultDto>(black.Client, $"/api/v1/matches/{matchId}/commands", resign);

        var replay = await red.Client.GetFromJsonAsync<ReplayPageDto>($"/api/v1/matches/{matchId}/replay?afterSequence=-1&limit=2", Json);
        Assert.Equal(new[] { 0, 1 }, replay!.Entries.Select(x => x.SequenceNo));
        Assert.NotNull(replay.NextAfterSequence);
        var replayTail = await red.Client.GetFromJsonAsync<ReplayPageDto>($"/api/v1/matches/{matchId}/replay?afterSequence={replay.NextAfterSequence}&limit=20", Json);
        Assert.Contains(replayTail!.Entries, x => x.Kind == "resign");
        var history = await red.Client.GetFromJsonAsync<MatchPageDto>("/api/v1/matches?limit=5", Json);
        Assert.Contains(history!.Items, x => x.MatchId == matchId && x.Status == "completed");
    }

    [Fact]
    // Bot_uses_same_pipeline_and_undo_appends_without_deleting_history: Kiểm tra nước bot đi qua pipeline và undo giữ lịch sử append-only.
    public async Task Bot_uses_same_pipeline_and_undo_appends_without_deleting_history()
    {
        var human = await CreatePlayerWithLineup(preferSpecialHeroes: true);
        var ticket = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        var matchId = ticket.MatchId!.Value;
        await Select(human, matchId);
        await human.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null);
        var initial = await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json);
        var legal = await human.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{matchId}/legal-actions", Json);
        var legalMoves = legal ?? throw new InvalidOperationException("Legal moves response was empty.");
        Assert.NotEmpty(legalMoves);
        var specialId = initial!.State.GetProperty("pieces").EnumerateArray()
            .Where(x => x.GetProperty("side").GetString() == "red" && x.TryGetProperty("movementImplementationKey", out var key) && key.GetString() == "elephant.alternating_distance")
            .Select(x => x.GetProperty("pieceId").GetGuid()).FirstOrDefault();
        Assert.NotEqual(Guid.Empty, specialId);
        var selectedMove = legalMoves.FirstOrDefault(x => x.PieceId == specialId) ?? legalMoves[0];
        var move = Command(initial.Version, selectedMove);
        var moved = await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{matchId}/commands", move);
        if (selectedMove.PieceId == specialId)
        {
            var specialAfterMove = moved.Snapshot.State.GetProperty("pieces").EnumerateArray().Single(x => x.GetProperty("pieceId").GetGuid() == specialId);
            Assert.True(specialAfterMove.GetProperty("traitState").TryGetProperty("lastMoveDistance", out _));
        }
        MatchStateDto afterBot = initial;
        for (var i = 0; i < 50 && afterBot.Version < 2; i++)
        {
            await Task.Delay(50);
            afterBot = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json))!;
        }
        Assert.True(afterBot.Version >= 2);
        var undo = new MatchCommandRequest(Guid.NewGuid(), afterBot.Version, JsonSerializer.SerializeToElement(new { type = "undo", targetSequence = 0 }, Json));
        var undone = await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{matchId}/commands", undo);
        Assert.True(undone.SequenceNo > afterBot.Version);
        Assert.Equal("red", undone.Snapshot.SideToMove);
        if (specialId != Guid.Empty)
        {
            var specialAfterUndo = undone.Snapshot.State.GetProperty("pieces").EnumerateArray().Single(x => x.GetProperty("pieceId").GetGuid() == specialId);
            Assert.Empty(specialAfterUndo.GetProperty("traitState").EnumerateObject().ToArray());
        }
        var resign = new MatchCommandRequest(Guid.NewGuid(), undone.Snapshot.Version, JsonSerializer.SerializeToElement(new { type = "resign" }, Json));
        await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{matchId}/commands", resign);
        var replay = await human.Client.GetFromJsonAsync<ReplayPageDto>($"/api/v1/matches/{matchId}/replay?afterSequence=-1&limit=100", Json);
        Assert.Contains(replay!.Entries, x => x.Kind == "undo");
        Assert.Contains(replay.Entries, x => x.SequenceNo == 1);
        Assert.Equal(replay.Entries.Count, replay.Entries.Select(x => x.SequenceNo).Distinct().Count());
    }

    [Fact]
    // Deadline_boundary_and_settlement_retries_are_idempotent: Kiểm tra ranh giới deadline và settlement không trả thưởng hai lần.
    public async Task Deadline_boundary_and_settlement_retries_are_idempotent()
    {
        var red = await CreatePlayerWithLineup(); var black = await CreatePlayerWithLineup();
        var before = await red.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        var redTicket = await Post<MatchmakingTicketDto>(red.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var blackTicket = await Post<MatchmakingTicketDto>(black.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var matchId = blackTicket.MatchId!.Value; await Select(red, matchId); await Select(black, matchId);
        await Task.WhenAll(red.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null), black.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null));
        var state = await red.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var timeouts = scope.ServiceProvider.GetRequiredService<MatchCommandService>();
            Assert.False(await timeouts.TimeoutAsync(matchId, 0, state!.DeadlineAt!.Value.AddTicks(-1), CancellationToken.None));
            Assert.True(await timeouts.TimeoutAsync(matchId, 0, state.DeadlineAt.Value, CancellationToken.None));
            Assert.False(await timeouts.TimeoutAsync(matchId, 0, state.DeadlineAt.Value.AddSeconds(1), CancellationToken.None));
        }
        var timedOut = await black.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json);
        Assert.Equal(1, timedOut!.Version); Assert.Equal(1, timedOut.CountedActions); Assert.Equal("black", timedOut.SideToMove);
        await Post<MatchCommandResultDto>(black.Client, $"/api/v1/matches/{matchId}/commands",
            new MatchCommandRequest(Guid.NewGuid(), timedOut.Version, JsonSerializer.SerializeToElement(new { type = "resign" }, Json)));
        var settledOnce = await red.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        Assert.Equal((long.Parse(before!.CoinBalance!) + 10).ToString(), settledOnce!.CoinBalance);
        // RetrySettlement: Hàm local: tạo scope mới để thử settlement độc lập.
        async Task RetrySettlement()
        {
            await using var retryScope = _factory.Services.CreateAsyncScope();
            await retryScope.ServiceProvider.GetRequiredService<SettlementService>().SettleAsync(matchId, CancellationToken.None);
        }
        await Task.WhenAll(RetrySettlement(), RetrySettlement());
        var settledAgain = await red.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        Assert.Equal(settledOnce.CoinBalance, settledAgain!.CoinBalance); Assert.Equal(settledOnce.Elo, settledAgain.Elo);
    }

    // CreatePlayerWithLineup: Đăng ký/đăng nhập player và tạo đội hình fixture theo class; có tùy chọn special hero.
    private async Task<(HttpClient Client, LineupDto Lineup)> CreatePlayerWithLineup(string? requestedEmail = null, bool preferSpecialHeroes = false)
    {
        var client = _factory.CreateClient(); var email = requestedEmail ?? $"match-{Guid.NewGuid():N}@hero.local"; var password = $"A1!{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password })).StatusCode);
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false", new { email, password })).Content.ReadFromJsonAsync<TokenResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        var catalog = await client.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json);
        var byClass = catalog!.Heroes.Where(x => x.IsOwned).GroupBy(x => x.ClassCode).ToDictionary(x => x.Key,
            x => new Queue<HeroDto>(x.OrderBy(h => preferSpecialHeroes
                ? !new[] { "tran-binh-trong-elephant", "bui-thi-xuan-elephant", "da-tuong-elephant" }.Contains(h.Code)
                : !h.Code.StartsWith("dev-slot-", StringComparison.Ordinal)).ThenBy(h => h.Code)));
        var entries = catalog.Slots.OrderBy(x => x.SlotNo).Select(x => new LineupEntryInput(x.SlotNo, byClass[x.ClassCode].Dequeue().Id, null)).ToArray();
        var skills = catalog.TeamSkills.Take(3).Select((x, i) => new LineupSkillInput(i + 1, x.Id)).ToArray();
        var lineup = await Post<LineupDto>(client, "/api/v1/lineups", new SaveLineupRequest("Match fixture", catalog.Ruleset.Id, entries, skills));
        return (client, lineup);
    }

    // Select: Gửi lựa chọn lineup/revision vào trận.
    private static Task Select((HttpClient Client, LineupDto Lineup) player, Guid matchId) =>
        Post<MatchSelectionDto>(player.Client, $"/api/v1/matches/{matchId}/selection", new SelectLineupRequest(player.Lineup.Id, player.Lineup.Revision), HttpMethod.Put);
    // Command: Tạo commandId mới và action move từ một legal move.
    private static MatchCommandRequest Command(int version, LegalMoveDto move) => new(Guid.NewGuid(), version,
        JsonSerializer.SerializeToElement(new { type = "move", pieceId = move.PieceId, to = new { x = move.ToX, y = move.ToY } }, Json));
    // Post: Gửi request JSON, assert thành công rồi deserialize response kiểu T.
    private static async Task<T> Post<T>(HttpClient client, string path, object body, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Json) };
        var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }
    // Connect: Xin vé rồi mở socket qua TestServer.
    private async Task<WebSocket> Connect(HttpClient client)
    {
        var ticket = await Post<WsTicketDto>(client, "/api/v1/ws-ticket", new { });
        var ws = _factory.Server.CreateWebSocketClient();
        return await ws.ConnectAsync(new Uri($"ws://localhost/ws/v1?ticket={Uri.EscapeDataString(ticket.Ticket)}"), CancellationToken.None);
    }
    // Subscribe: Gửi envelope match.subscribe.
    private static Task Subscribe(WebSocket socket, Guid matchId) => socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "match.subscribe", requestId = Guid.NewGuid().ToString(), payload = new { matchId } }, Json)), WebSocketMessageType.Text, true, CancellationToken.None);
    // Receive: Ghép các fragment socket thành một JSON message với timeout.
    private static async Task<JsonElement> Receive(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); using var stream = new MemoryStream(); var buffer = new byte[8192]; WebSocketReceiveResult result;
        do { result = await socket.ReceiveAsync(buffer, cts.Token); stream.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
        using var doc = JsonDocument.Parse(stream.ToArray()); return doc.RootElement.Clone();
    }
    private sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);
}


// Vai trò file: Regression cho quyền socket, retry, selection, input sai và phục hồi công việc sau commit.
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class MatchFlowTests
{
    // Ranked: Chuẩn bị hai player, ghép trận, chọn đội hình và tùy chọn confirm.
    private async Task<(Guid Id, (HttpClient Client, LineupDto Lineup) Red, (HttpClient Client, LineupDto Lineup) Black)> Ranked(bool confirm = true)
    {
        var red = await CreatePlayerWithLineup(); var black = await CreatePlayerWithLineup();
        await Post<MatchmakingTicketDto>(red.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var ticket = await Post<MatchmakingTicketDto>(black.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var id = ticket.MatchId!.Value;
        await Select(red, id); await Select(black, id);
        if (confirm)
        {
            Assert.Equal(HttpStatusCode.OK, (await red.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await black.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).StatusCode);
        }
        return (id, red, black);
    }
    // SendWs: Gửi command JSON qua WebSocket.
    private static Task SendWs(WebSocket socket, Guid id, MatchCommandRequest command) => socket.SendAsync(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "match.command", payload = new { matchId = id, command.CommandId, command.ExpectedVersion, command.Action } }, Json)),
        WebSocketMessageType.Text, true, CancellationToken.None);

    [Fact]
    // Outsider_command_does_not_subscribe_and_socket_retry_gets_ack: Chặn người ngoài nghe trận; command retry vẫn nhận ACK.
    public async Task Outsider_command_does_not_subscribe_and_socket_retry_gets_ack()
    {
        var (id, red, black) = await Ranked(); var outsider = await CreatePlayerWithLineup();
        using var outsiderSocket = await Connect(outsider.Client);
        var rejected = new MatchCommandRequest(Guid.NewGuid(), 0, JsonSerializer.SerializeToElement(new { type = "resign" }));
        await SendWs(outsiderSocket, id, rejected);
        Assert.Equal("MATCH_NOT_FOUND", (await Receive(outsiderSocket)).GetProperty("payload").GetProperty("code").GetString());
        using var socket = await Connect(red.Client);
        var moves = await red.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{id}/legal-actions", Json);
        var command = Command(0, moves![0]);
        await SendWs(socket, id, command);
        Assert.Equal("match.command_accepted", (await Receive(socket)).GetProperty("type").GetString());
        using var retrySocket = await Connect(red.Client);
        await SendWs(retrySocket, id, command);
        var ack = (await Receive(retrySocket)).GetProperty("payload");
        Assert.True(ack.GetProperty("duplicate").GetBoolean()); Assert.Equal(1, ack.GetProperty("sequenceNo").GetInt32());
        await Post<MatchCommandResultDto>(black.Client, $"/api/v1/matches/{id}/commands", rejected with { CommandId = Guid.NewGuid(), ExpectedVersion = 1 });
        // A direct rejection acts as a FIFO barrier after all match broadcasts have been enqueued.
        await SendWs(outsiderSocket, id, rejected);
        Assert.Equal("match.command_rejected", (await Receive(outsiderSocket)).GetProperty("type").GetString());
    }

    [Fact]
    // Confirm_is_idempotent_after_source_delete_and_selection_can_be_cancelled: Confirm lặp vẫn hợp lệ khi lineup nguồn bị xóa; selection có thể hủy.
    public async Task Confirm_is_idempotent_after_source_delete_and_selection_can_be_cancelled()
    {
        var (id, red, black) = await Ranked(false);
        Assert.Equal(HttpStatusCode.OK, (await red.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await red.Client.DeleteAsync($"/api/v1/lineups/{red.Lineup.Id}?expectedRevision={red.Lineup.Revision}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await red.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).StatusCode);
        var outsider = await CreatePlayerWithLineup();
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.DeleteAsync($"/api/v1/matches/{id}/selection")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await red.Client.DeleteAsync($"/api/v1/matches/{id}/selection")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await red.Client.DeleteAsync($"/api/v1/matches/{id}/selection")).StatusCode);
        foreach (var player in new[] { red, black })
        {
            var ticket = await Post<MatchmakingTicketDto>(player.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
            await player.Client.DeleteAsync($"/api/v1/matches/{ticket.MatchId}/selection");
        }
    }

    [Fact]
    // Malformed_actions_return_400_without_state_changes: JSON sai kiểu/shape trả 400 và không sửa state.
    public async Task Malformed_actions_return_400_without_state_changes()
    {
        var (id, red, black) = await Ranked();
        foreach (var json in new[] { "null", "[]", "{\"type\":3}", "{\"type\":\"move\",\"pieceId\":\"bad\",\"to\":{\"x\":0,\"y\":1}}", "{\"type\":\"undo\",\"targetSequence\":\"x\"}" })
        {
            var request = new MatchCommandRequest(Guid.NewGuid(), 0, JsonSerializer.Deserialize<JsonElement>(json));
            Assert.Equal(HttpStatusCode.BadRequest, (await red.Client.PostAsJsonAsync($"/api/v1/matches/{id}/commands", request, Json)).StatusCode);
        }
        var state = await red.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json);
        Assert.Equal(0, state!.Version);
        await Post<MatchCommandResultDto>(black.Client, $"/api/v1/matches/{id}/commands", new MatchCommandRequest(Guid.NewGuid(), 0, JsonSerializer.SerializeToElement(new { type = "resign" })));
    }

    private sealed class CancelAfterCommit(CancellationTokenSource request) : DbTransactionInterceptor
    {
        // TransactionCommittedAsync: Interceptor test: hủy request ngay sau khi DB commit để tái hiện race.
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { request.Cancel(); return Task.CompletedTask; }
    }

    [Fact]
    // Maintenance_recovers_bot_turn_when_original_schedule_signal_is_lost: Mất tín hiệu schedule ban đầu vẫn được maintenance phát hiện lượt bot.
    public async Task Maintenance_recovers_bot_turn_when_original_schedule_signal_is_lost()
    {
        var human = await CreatePlayerWithLineup();
        var ticket = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        var id = ticket.MatchId!.Value; await Select(human, id);
        (await human.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
        var me = (await human.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        var moves = (await human.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{id}/legal-actions", Json))!;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            // This scheduler intentionally never starts: its original enqueue signal is lost.
            using var stoppedBot = new BotTurnScheduler(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILogger<BotTurnScheduler>>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MatchRuntimeOptions>>());
            var command = new MatchCommandService(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<MatchLockRegistry>(),
                sp.GetRequiredService<SettlementService>(), sp.GetRequiredService<MatchConnectionHub>(), TimeProvider.System, stoppedBot,
                sp.GetRequiredService<ILogger<MatchCommandService>>());
            await command.ExecuteAsync(me.PlayerId!.Value, id, Command(0, moves[0]), null, CancellationToken.None);
        }
        await _factory.Services.GetRequiredService<MatchMaintenanceHostedService>().SweepAsync(CancellationToken.None);
        MatchStateDto state = null!;
        for (var i = 0; i < 60; i++)
        {
            state = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
            if (state.Version >= 2) break;
            await Task.Delay(50);
        }
        Assert.Equal(2, state.Version); Assert.Equal("red", state.SideToMove);
        await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{id}/commands",
            new MatchCommandRequest(Guid.NewGuid(), state.Version, JsonSerializer.SerializeToElement(new { type = "resign" })));
    }

    private sealed class FailSettlementOnce : DbCommandInterceptor
    {
        private bool _failed;
        // ReaderExecutingAsync: Interceptor test: cố ý làm lỗi một lần khi settlement lock wallet.
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!_failed && command.CommandText.Contains("player_wallet") && command.CommandText.Contains("FOR UPDATE"))
            { _failed = true; throw new InvalidOperationException("Injected settlement failure after match commit"); }
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // Request_cancelled_exactly_after_commit_still_settles_and_releases_players: Kiểm tra hủy request sau commit không bỏ thưởng hoặc giữ player bận, kể cả settlement lỗi lần đầu.
    public async Task Request_cancelled_exactly_after_commit_still_settles_and_releases_players(bool failFirstSettlement)
    {
        var (id, red, black) = await Ranked();
        var before = (await red.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        var blackMe = (await black.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        using var requestCancellation = new CancellationTokenSource();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("HERO_CHESS_TEST_DB"))
            .AddInterceptors(new CancelAfterCommit(requestCancellation));
        if (failFirstSettlement) options.AddInterceptors(new FailSettlementOnce());
        await using var db = new AppDbContext(options.Options);
        await using var scope = _factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var hub = sp.GetRequiredService<MatchConnectionHub>(); var matchmaking = sp.GetRequiredService<MatchmakingService>();
        var settlement = new SettlementService(db, TimeProvider.System, matchmaking, sp.GetRequiredService<RewardPolicy>(), sp.GetRequiredService<RatingPolicy>(), hub);
        var service = new MatchCommandService(db, sp.GetRequiredService<MatchLockRegistry>(), settlement, hub, TimeProvider.System,
            sp.GetRequiredService<BotTurnScheduler>(), sp.GetRequiredService<ILogger<MatchCommandService>>());
        var request = new MatchCommandRequest(Guid.NewGuid(), 0, JsonSerializer.SerializeToElement(new { type = "resign" }));
        await service.ExecuteAsync(blackMe.PlayerId!.Value, id, request, null, requestCancellation.Token);
        Assert.True(requestCancellation.IsCancellationRequested);
        if (failFirstSettlement)
            await _factory.Services.GetRequiredService<MatchMaintenanceHostedService>().SweepAsync(CancellationToken.None);
        var after = (await red.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        Assert.Equal(long.Parse(before.CoinBalance!) + 10, long.Parse(after.CoinBalance!));
        var retry = await Post<MatchCommandResultDto>(black.Client, $"/api/v1/matches/{id}/commands", request);
        Assert.True(retry.Duplicate);
        var next = await Post<MatchmakingTicketDto>(black.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        await black.Client.DeleteAsync($"/api/v1/matches/{next.MatchId}/selection");
    }

    [Fact]
    // Maintenance_expires_selection_and_recovers_terminal_zero_policy_without_restart: Worker hủy selection hết hạn và settle terminal với policy 0 mà không cần restart.
    public async Task Maintenance_expires_selection_and_recovers_terminal_zero_policy_without_restart()
    {
        var (expiredId, expiredRed, _) = await Ranked(false);
        var (terminalId, red, black) = await Ranked();
        var before = (await red.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expired = await db.Matches.SingleAsync(x => x.Id == expiredId); expired.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var terminal = await db.Matches.SingleAsync(x => x.Id == terminalId);
            terminal.Status = "completed"; terminal.Result = "red_win"; terminal.EndReason = "resign"; terminal.EndedAt = DateTimeOffset.UtcNow;
            var frozen = GameJson.Read<RulesetSnapshot>(terminal.RulesetSnapshot);
            terminal.RulesetSnapshot = GameJson.Document(frozen with { WinnerCoinReward = 0, DrawCoinReward = 0, EloKFactor = 0 });
            await db.SaveChangesAsync();
        }
        await _factory.Services.GetRequiredService<MatchMaintenanceHostedService>().SweepAsync(CancellationToken.None);
        await _factory.Services.GetRequiredService<MatchMaintenanceHostedService>().SweepAsync(CancellationToken.None);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull((await db.Matches.SingleAsync(x => x.Id == terminalId)).SettledAt);
            Assert.Equal("selection_timeout", (await db.Matches.SingleAsync(x => x.Id == expiredId)).EndReason);
        }
        var after = (await red.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        Assert.Equal(before.CoinBalance, after.CoinBalance); Assert.Equal(before.Elo, after.Elo);
        foreach (var player in new[] { expiredRed, red, black })
        {
            var next = await Post<MatchmakingTicketDto>(player.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
            await player.Client.DeleteAsync($"/api/v1/matches/{next.MatchId}/selection");
        }
    }
}

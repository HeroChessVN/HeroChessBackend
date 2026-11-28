using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class TrafficLimitsTests
{
    [Fact]
    public void Socket_and_work_slots_are_atomic_reusable_and_failed_global_acquire_does_not_leak()
    {
        using var limits = new TrafficLimits(Options.Create(new TrafficLimitsOptions { MaxSocketsPerAccount = 2, MaxSockets = 2, MaxHttpInFlight = 1 }));
        var first = Guid.NewGuid(); var other = Guid.NewGuid();
        var one = limits.OpenSocket(first); var two = limits.OpenSocket(first);
        Assert.Equal("SOCKET_LIMIT", Assert.Throws<ApiException>(() => limits.OpenSocket(first)).Code);
        Assert.Equal(429, Assert.Throws<ApiException>(() => limits.OpenSocket(other)).StatusCode);
        one.Dispose(); one.Dispose();
        using var replacement = limits.OpenSocket(other);
        Assert.Throws<ApiException>(() => limits.OpenSocket(other)); // A double disposal must not create extra permits.
        two.Dispose();
        using var next = limits.OpenSocket(other); // The failed global acquisition released its account permit.
        using (limits.EnterHttp()) Assert.Equal("SERVER_BUSY", Assert.Throws<ApiException>(() => limits.EnterHttp()).Code);
        using var recovered = limits.EnterHttp();
    }

    [Fact]
    public void Shared_account_budget_cannot_be_reset_by_switching_lane_or_account_header()
    {
        var config = new TrafficLimitsOptions();
        config.Account.TokenLimit = 2; config.Account.TokensPerPeriod = 2; config.Account.ReplenishmentPeriod = TimeSpan.FromHours(1);
        using var limits = new TrafficLimits(Options.Create(config));
        var account = Guid.NewGuid();
        Parallel.For(0, 2, _ => limits.CheckAccount(account));
        var rejected = Assert.Throws<ApiException>(() => limits.CheckAccount(account));
        Assert.Equal(429, rejected.StatusCode); Assert.True(rejected.RetryAfterSeconds > 0);
        limits.CheckAccount(Guid.NewGuid());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Global_budget_is_shared_between_rest_and_socket_messages(bool restFirst)
    {
        var config = new TrafficLimitsOptions();
        config.Global.TokenLimit = 1; config.Global.TokensPerPeriod = 1; config.Global.ReplenishmentPeriod = TimeSpan.FromHours(1);
        using var limits = new TrafficLimits(Options.Create(config));
        var account = Guid.NewGuid();
        if (restFirst)
        {
            limits.CheckAddress("203.0.113.1");
            Assert.Equal("SERVER_BUSY", Assert.Throws<ApiException>(() => limits.CheckMessage(account)).Code);
        }
        else
        {
            limits.CheckMessage(account);
            Assert.Equal("SERVER_BUSY", Assert.Throws<ApiException>(() => limits.CheckAddress("203.0.113.1")).Code);
        }
    }

    [Fact]
    public void Tickets_are_bounded_one_time_and_expired_entries_free_capacity()
    {
        var clock = new TicketClock();
        var tickets = new WsTicketService(Options.Create(new MatchRuntimeOptions()), clock,
            Options.Create(new TrafficLimitsOptions { MaxPendingTickets = 5 }));
        var account = Guid.NewGuid();
        var issued = Enumerable.Range(0, 4).Select(_ => tickets.Issue(account)).ToArray();
        Assert.Equal("TICKET_LIMIT", Assert.Throws<ApiException>(() => tickets.Issue(account)).Code);
        var other = tickets.Issue(Guid.NewGuid());
        Assert.Throws<ApiException>(() => tickets.Issue(Guid.NewGuid()));
        var consumed = 0;
        Parallel.For(0, 20, _ => { if (tickets.TryConsume(issued[0].Ticket, out var player)) { Assert.Equal(account, player); Interlocked.Increment(ref consumed); } });
        Assert.Equal(1, consumed);
        tickets.Issue(account);
        clock.Now += TimeSpan.FromSeconds(31);
        Assert.False(tickets.TryConsume(other.Ticket, out _));
        Assert.NotEmpty(tickets.Issue(account).Ticket);
    }

    [Fact]
    public void Invalid_limits_cannot_start_with_disabled_replenishment_or_waiting_queues()
    {
        var config = new TrafficLimitsOptions(); Assert.True(config.IsValid());
        config.Reads.QueueLimit = 1; Assert.False(config.IsValid());
        config.Reads.QueueLimit = 0; config.Reads.AutoReplenishment = false; Assert.False(config.IsValid());
        config.Reads.AutoReplenishment = true; config.MaxSockets = 0; Assert.False(config.IsValid());
    }

    [Fact]
    public async Task Failed_or_cancelled_request_releases_work_slot()
    {
        using var limits = new TrafficLimits(Options.Create(new TrafficLimitsOptions { MaxHttpInFlight = 1 }));
        foreach (var failure in new Exception[] { new InvalidOperationException(), new OperationCanceledException() })
        {
            var context = new DefaultHttpContext(); context.Request.Path = "/api/v1/me";
            var middleware = new TrafficLimitsMiddleware(_ => Task.FromException(failure));
            Assert.Same(failure, await Assert.ThrowsAnyAsync<Exception>(() => middleware.InvokeAsync(context, limits)));
            using var next = limits.EnterHttp();
        }
    }

    [Theory]
    [InlineData(400, "BAD_REQUEST")]
    [InlineData(413, "REQUEST_TOO_LARGE")]
    public async Task Native_http_validation_preserves_client_status_without_exposing_exception_detail(int status, string code)
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var middleware = new ApiExceptionMiddleware(_ => throw new BadHttpRequestException("private parser detail", status),
            NullLogger<ApiExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains(code, body); Assert.DoesNotContain("private parser detail", body);
    }

    [Theory]
    [InlineData(true, 499)]
    [InlineData(false, 500)]
    public async Task Only_client_cancellation_is_treated_as_aborted_request(bool clientAborted, int status)
    {
        using var cancellation = new CancellationTokenSource(); if (clientAborted) cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token }; context.Response.Body = new MemoryStream();
        var middleware = new ApiExceptionMiddleware(_ => throw new OperationCanceledException(), NullLogger<ApiExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        if (clientAborted) Assert.Equal(0, context.Response.Body.Length);
    }

    private sealed class TicketClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

public sealed class TrafficLimitsApiTests(HeroChessFactory factory) : IClassFixture<HeroChessFactory>
{
    [Theory]
    [InlineData("null")]
    [InlineData("not-an-origin")]
    [InlineData("https://untrusted.example")]
    public async Task Malformed_or_foreign_origin_is_rejected_before_consuming_ticket(string origin)
    {
        using var host = Limited(_ => { });
        var tickets = host.Services.GetRequiredService<WsTicketService>();
        var issued = tickets.Issue(Guid.NewGuid());
        var socket = host.Server.CreateWebSocketClient();
        socket.ConfigureRequest = request => request.Headers.Origin = origin;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => socket.ConnectAsync(
            new Uri("ws://localhost/ws/v1?ticket=" + issued.Ticket), CancellationToken.None));
        Assert.Contains("403", error.Message);
        Assert.True(tickets.TryConsume(issued.Ticket, out _));
    }

    [Fact]
    public async Task Rest_and_socket_reads_share_account_budget_and_429_contains_retry_after()
    {
        using var host = Limited(options => SetBucket(options.Reads, 2));
        using var client = await Register(host);
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/matches/{Guid.NewGuid()}/state")).StatusCode);
        using var response = await client.GetAsync($"/api/v1/matches/{Guid.NewGuid()}/state");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Contains("RATE_LIMITED", await response.Content.ReadAsStringAsync());
        using var socket = await Connect(host, client);
        var requestId = Guid.NewGuid().ToString();
        await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "match.subscribe", requestId, payload = new { matchId = Guid.NewGuid() } })),
            WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = await socket.ReceiveAsync(buffer, timeout.Token);
        using var error = JsonDocument.Parse(buffer.AsMemory(0, read.Count));
        Assert.Equal(requestId, error.RootElement.GetProperty("requestId").GetString());
        Assert.Equal("RATE_LIMITED", error.RootElement.GetProperty("payload").GetProperty("code").GetString());
        Assert.True(error.RootElement.GetProperty("payload").GetProperty("details").GetProperty("retryAfterSeconds").GetInt32() > 0);
        using var other = await Register(host);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/matches/{Guid.NewGuid()}/state")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
        socket.Abort();
    }

    [Fact]
    public async Task Ticket_issuance_is_limited_without_blocking_profile_or_another_account()
    {
        using var host = Limited(options => SetBucket(options.Tickets, 2));
        using var client = await Register(host);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/ws-ticket", null)).StatusCode);
        using var response = await client.PostAsync("/API/v1/WS-TICKET/", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
        using var other = await Register(host);
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsync("/api/v1/ws-ticket", null)).StatusCode);
    }

    [Fact]
    public async Task Socket_limit_rejects_before_upgrade_and_disconnect_allows_reconnect()
    {
        using var host = Limited(_ => { });
        using var client = await Register(host);
        using var first = await Connect(host, client);
        using var second = await Connect(host, client);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Connect(host, client));
        Assert.Contains("429", error.Message);
        first.Abort();
        // TestServer may finish the receive loop asynchronously after the client aborts.
        WebSocket? replacement = null;
        for (var i = 0; i < 20 && replacement is null; i++)
        {
            try { replacement = await Connect(host, client); }
            catch (InvalidOperationException failure) when (failure.Message.Contains("429")) { await Task.Delay(50); }
        }
        Assert.NotNull(replacement); replacement.Abort(); replacement.Dispose(); second.Abort();
    }

    [Fact]
    public async Task Busy_handshake_releases_socket_reservation_and_open_socket_does_not_hold_work_slot()
    {
        using var host = Limited(options => { options.MaxSocketsPerAccount = 1; options.MaxSockets = 1; options.MaxHttpInFlight = 1; });
        using var client = await Register(host);
        var me = (await client.GetFromJsonAsync<MeDto>("/api/v1/me"))!;
        var ticket = host.Services.GetRequiredService<WsTicketService>().Issue(me.UserId);
        using (host.Services.GetRequiredService<TrafficLimits>().EnterHttp())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Server.CreateWebSocketClient().ConnectAsync(
                new Uri("ws://localhost/ws/v1?ticket=" + ticket.Ticket), CancellationToken.None));
            Assert.Contains("429", error.Message);
        }
        using var socket = await Connect(host, client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
        socket.Abort();
    }

    [Theory]
    [InlineData(65537, WebSocketMessageType.Text)]
    [InlineData(1, WebSocketMessageType.Binary)]
    public async Task Oversized_or_binary_socket_message_releases_slot_without_waiting_for_peer_close_handshake(int size, WebSocketMessageType type)
    {
        using var host = Limited(options => { options.MaxSocketsPerAccount = 1; options.MaxSockets = 1; });
        using var client = await Register(host);
        using var socket = await Connect(host, client);
        await socket.SendAsync(new byte[size], type, true, CancellationToken.None);
        WebSocket? replacement = null;
        for (var i = 0; i < 20 && replacement is null; i++)
        {
            try { replacement = await Connect(host, client); }
            catch (InvalidOperationException failure) when (failure.Message.Contains("429")) { await Task.Delay(50); }
        }
        Assert.NotNull(replacement); replacement.Abort(); replacement.Dispose();
    }

    [Fact]
    public async Task Forged_forwarded_ip_does_not_bypass_ip_limit_and_health_remains_available()
    {
        using var host = Limited(options => SetBucket(options.Ip, 2));
        using var client = host.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var response = await host.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
                context.Request.Method = "GET"; context.Request.Path = "/api/v1/me";
                context.Request.Headers["X-Forwarded-For"] = $"203.0.113.{i + 1}";
            });
            Assert.Equal(i < 2 ? 401 : 429, response.Response.StatusCode);
            Assert.Equal("198.51.100.1", response.Connection.RemoteIpAddress?.ToString());
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Only_trusted_proxy_can_forward_client_ip_and_https_scheme()
    {
        using var host = Limited(options => SetBucket(options.Ip, 1));
        for (var i = 0; i < 2; i++)
        {
            var response = await host.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("172.17.0.1");
                context.Request.Method = "GET"; context.Request.Path = "/api/v1/me";
                context.Request.Headers["X-Forwarded-For"] = $"192.0.2.1, 203.0.113.{i + 1}";
                context.Request.Headers["X-Forwarded-Proto"] = "https";
            });
            Assert.Equal(401, response.Response.StatusCode);
            Assert.Equal($"203.0.113.{i + 1}", response.Connection.RemoteIpAddress?.ToString());
            Assert.Equal("https", response.Request.Scheme);
        }
    }

    [Fact]
    public async Task Anonymous_auth_requests_have_separate_quota_from_profile_requests()
    {
        using var host = Limited(options => SetBucket(options.Authentication, 1));
        using var client = host.CreateClient();
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/auth/login", new { })).StatusCode);
        using var blocked = await client.PostAsJsonAsync("/api/v1/auth/register", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    private WebApplicationFactory<Program> Limited(Action<TrafficLimitsOptions> configure) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services => services.PostConfigure(configure)));
    private static void SetBucket(System.Threading.RateLimiting.TokenBucketRateLimiterOptions bucket, int capacity)
    { bucket.TokenLimit = capacity; bucket.TokensPerPeriod = capacity; bucket.ReplenishmentPeriod = TimeSpan.FromHours(1); }

    private static async Task<HttpClient> Register(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var credentials = new { email = $"traffic-{Guid.NewGuid():N}@hero.local", password = $"A1!{Guid.NewGuid():N}" };
        (await client.PostAsJsonAsync("/api/v1/auth/register", credentials)).EnsureSuccessStatusCode();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false", credentials);
        login.EnsureSuccessStatusCode();
        using var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<WebSocket> Connect(WebApplicationFactory<Program> host, HttpClient client)
    {
        using var response = await client.PostAsync("/api/v1/ws-ticket", null);
        response.EnsureSuccessStatusCode();
        var ticket = (await response.Content.ReadFromJsonAsync<WsTicketDto>())!;
        return await host.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws/v1?ticket=" + ticket.Ticket), CancellationToken.None);
    }
}

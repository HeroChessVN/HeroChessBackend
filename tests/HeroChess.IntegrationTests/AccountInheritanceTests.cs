using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using HeroChess.Api.Auth;
using HeroChess.Api.Data;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;

// Kiểm tra subtype thật trong EF, onboarding riêng và phân quyền kể cả khi claim/vé bị cũ.
public sealed class AccountInheritanceTests(HeroChessFactory factory) : IClassFixture<HeroChessFactory>
{
    [Fact]
    public async Task Admin_is_a_user_without_player_resources_and_cannot_use_gameplay_routes()
    {
        var (client, profile, refresh) = await Register(factory.DevelopmentAdminEmail);
        Assert.Equal("admin", profile.Role);
        Assert.Null(profile.PlayerId); Assert.Null(profile.CoinBalance); Assert.Null(profile.Elo); Assert.Null(profile.IsGuest);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.IsType<Admin>(await db.Users.SingleAsync(x => x.Id == profile.UserId));
        Assert.False(await db.Players.AnyAsync(x => x.Id == profile.UserId));
        Assert.False(await db.Wallets.AnyAsync(x => x.PlayerId == profile.UserId));
        Assert.False(await db.Ratings.AnyAsync(x => x.PlayerId == profile.UserId));
        Assert.False(await db.PlayerHeroes.AnyAsync(x => x.PlayerId == profile.UserId));
        Assert.False(await db.CoinTransactions.AnyAsync(x => x.PlayerId == profile.UserId));
        Assert.True(await db.AuthIdentities.AnyAsync(x => x.UserId == profile.UserId));

        var id = Guid.NewGuid();
        foreach (var (method, route) in new[] {
            (HttpMethod.Get, "/api/v1/lineups"), (HttpMethod.Get, "/api/v1/me/heroes"),
            (HttpMethod.Post, "/api/v1/matchmaking/tickets"), (HttpMethod.Get, "/api/v1/matches"),
            (HttpMethod.Get, $"/api/v1/matches/{id}/selection"), (HttpMethod.Get, $"/api/v1/matches/{id}/state"),
            (HttpMethod.Post, $"/api/v1/matches/{id}/commands"), (HttpMethod.Post, "/api/v1/ws-ticket"),
            (HttpMethod.Post, $"/api/v1/shop/heroes/{id}/purchase") })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(new HttpRequestMessage(method, route))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh })).StatusCode);

        // Một vé phát hành trước khi đổi loại tài khoản cũng không mở được socket.
        var ticket = scope.ServiceProvider.GetRequiredService<WsTicketService>().Issue(profile.UserId);
        var socketClient = factory.Server.CreateWebSocketClient();
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => socketClient.ConnectAsync(
            new Uri("ws://localhost/ws/v1?ticket=" + ticket.Ticket), CancellationToken.None));
        Assert.Contains("403", denied.Message);
    }

    [Fact]
    public async Task Public_registration_cannot_choose_admin_and_policies_check_database_not_stale_claims()
    {
        var (client, profile, _) = await Register($"player-{Guid.NewGuid():N}@hero.local", spoofAdmin: true);
        Assert.Equal("player", profile.Role); Assert.Equal(profile.UserId, profile.PlayerId);
        Assert.NotNull(profile.CoinBalance); Assert.NotNull(profile.Elo);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/audit")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.IsType<Player>(await db.Users.SingleAsync(x => x.Id == profile.UserId));
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var forgedRole = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, profile.UserId.ToString()), new Claim(ClaimTypes.Role, "admin") }, "test"));
        Assert.False((await authorization.AuthorizeAsync(forgedRole, null, AccountPolicies.Admin)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(forgedRole, null, AccountPolicies.Player)).Succeeded);
        await db.Users.Where(x => x.Id == profile.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "disabled"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/lineups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    // Đăng ký qua endpoint công khai; role truyền thêm phải bị bỏ qua, admin chỉ đến từ config server Development.
    private async Task<(HttpClient Client, MeDto Profile, string Refresh)> Register(string email, bool spoofAdmin = false)
    {
        var client = factory.CreateClient(); var password = $"A1!{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, role = spoofAdmin ? "admin" : "player" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = (await login.Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (client, (await client.GetFromJsonAsync<MeDto>("/api/v1/me"))!, tokens.RefreshToken);
    }

    private sealed record Tokens(string AccessToken, string RefreshToken);
}

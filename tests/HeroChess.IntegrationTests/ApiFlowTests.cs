// Vai trò file: Kiểm thử HTTP đăng ký/đăng nhập/catalog/lineup. HeroChessFactory khởi động API trong TestServer với database test được chỉ định.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class ApiFlowTests : IClassFixture<HeroChessFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ApiFlowTests: Nhận fixture và tạo HttpClient gọi API in-process.
    public ApiFlowTests(HeroChessFactory factory) => _client = factory.CreateClient();

    [Fact]
    // Registered_player_can_authenticate_read_catalog_and_save_lineup_atomically: Kiểm tra auth, refresh token, catalog, tạo/sửa đội hình, revision conflict và validation.
    public async Task Registered_player_can_authenticate_read_catalog_and_save_lineup_atomically()
    {
        var web = await _client.GetStringAsync("/");
        Assert.Contains("Hero Chess prototype", web);
        var anonymous = await _client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var email = $"integration-{Guid.NewGuid():N}@hero.local";
        var credentials = new { email, password = $"A1!{Guid.NewGuid():N}" };
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v1/auth/register", credentials)).StatusCode);

        var login = await (await _client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false", credentials))
            .Content.ReadFromJsonAsync<TokenResponse>(Json);
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var me = await _client.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        var catalog = await _client.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json);
        Assert.NotNull(me);
        Assert.NotNull(catalog);
        Assert.True(catalog.Heroes.Count(x => x.IsOwned) >= 16);
        Assert.All(new[] { "tran-binh-trong-elephant", "bui-thi-xuan-elephant", "da-tuong-elephant" },
            code => Assert.Contains(catalog.Heroes, x => x.Code == code && x.IsOwned));

        var heroesByClass = catalog.Heroes.Where(x => x.IsOwned).GroupBy(x => x.ClassCode)
            .ToDictionary(x => x.Key, x => new Queue<HeroDto>(x.OrderBy(hero => !hero.Code.StartsWith("dev-slot-", StringComparison.Ordinal)).ThenBy(hero => hero.Code)));
        var entries = catalog.Slots.OrderBy(x => x.SlotNo)
            .Select(slot => new LineupEntryInput(slot.SlotNo, heroesByClass[slot.ClassCode].Dequeue().Id, null)).ToArray();
        var skills = catalog.TeamSkills.Take(3).Select((x, index) => new LineupSkillInput(index + 1, x.Id)).ToArray();
        var createRequest = new SaveLineupRequest("Integration lineup", catalog.Ruleset.Id, entries, skills);
        var create = await _client.PostAsJsonAsync("/api/v1/lineups", createRequest, Json);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var lineup = await create.Content.ReadFromJsonAsync<LineupDto>(Json);
        Assert.NotNull(lineup);
        Assert.True(lineup.IsValid);
        Assert.Equal(43, lineup.TotalSp);

        var update = createRequest with { Name = "Revision 2", ExpectedRevision = lineup.Revision };
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/v1/lineups/{lineup.Id}", update, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync($"/api/v1/lineups/{lineup.Id}", update, Json)).StatusCode);

        var invalid = createRequest with { Name = "Invalid", Entries = Array.Empty<LineupEntryInput>() };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.PostAsJsonAsync("/api/v1/lineups", invalid, Json)).StatusCode);

        var refresh = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { login.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    private sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);
}

public sealed class HeroChessFactory : WebApplicationFactory<Program>
{
    public string DevelopmentAdminEmail { get; } = $"admin-{Guid.NewGuid():N}@hero.local";

    // HeroChessFactory: Đòi hỏi HERO_CHESS_TEST_DB; cấu hình bootstrap fixture, coin và email admin cho môi trường test.
    public HeroChessFactory()
    {
        var connectionString = Environment.GetEnvironmentVariable("HERO_CHESS_TEST_DB") ??
            throw new InvalidOperationException("Set HERO_CHESS_TEST_DB to a disposable PostgreSQL database before running integration tests.");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);
        Environment.SetEnvironmentVariable("DatabaseBootstrap__Enabled", "true");
        Environment.SetEnvironmentVariable("DatabaseBootstrap__SeedDevelopmentFixtures", "true");
        Environment.SetEnvironmentVariable("Onboarding__GrantDevelopmentFixtureHeroes", "true");
        Environment.SetEnvironmentVariable("Onboarding__DevelopmentStartingCoins", "1000");
        Environment.SetEnvironmentVariable("Onboarding__DevelopmentAdminEmails__0", DevelopmentAdminEmail);
    }

    // ConfigureWebHost: Đặt môi trường Development để host test dùng các thiết lập tương ứng.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}

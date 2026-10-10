// Vai trò file: Kiểm thử purchase nguyên tử/idempotent và role admin/audit giá.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class ShopAdminTests : IClassFixture<HeroChessFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HeroChessFactory _factory;
    // ShopAdminTests: Nhận factory dùng chung môi trường test.
    public ShopAdminTests(HeroChessFactory factory) => _factory = factory;

    [Fact]
    // Purchase_is_atomic_idempotent_and_admin_price_changes_are_audited: Kiểm tra trừ coin/ownership, retry cùng key, quyền admin và giá trước/sau trong audit.
    public async Task Purchase_is_atomic_idempotent_and_admin_price_changes_are_audited()
    {
        var player = await Register();
        var catalog = await player.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json);
        var paid = catalog!.Heroes.Single(x => x.Code == "dev-shop-soldier");
        var free = catalog.Heroes.Single(x => x.Code == "dev-free-soldier");
        var expensive = catalog.Heroes.Single(x => x.Code == "dev-expensive-soldier");
        var disabledId = Guid.Parse("60000000-0000-4000-8000-000000000047");
        var setupPoints = paid.SetupPoints;

        Assert.Equal(HttpStatusCode.Forbidden, (await player.PatchAsJsonAsync($"/api/v1/admin/heroes/{paid.Id}/price", new UpdatePriceRequest("123", "forbidden"), Json)).StatusCode);

        var paidKey = Guid.NewGuid();
        var concurrent = await Task.WhenAll(Purchase(player, paid.Id, paidKey), Purchase(player, paid.Id, paidKey));
        Assert.All(concurrent, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var first = await concurrent[0].Content.ReadFromJsonAsync<PurchaseHeroDto>(Json);
        var second = await concurrent[1].Content.ReadFromJsonAsync<PurchaseHeroDto>(Json);
        Assert.Equal(first, second); Assert.Equal("100", first!.Charged); Assert.Equal("900", first.CoinBalance);
        Assert.Equal(HttpStatusCode.Conflict, (await Purchase(player, paid.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Purchase(player, free.Id, paidKey)).StatusCode);

        var freeKey = Guid.NewGuid();
        var freeResult = await (await Purchase(player, free.Id, freeKey)).Content.ReadFromJsonAsync<PurchaseHeroDto>(Json);
        Assert.Equal("0", freeResult!.Charged); Assert.Equal("900", freeResult.CoinBalance);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Purchase(player, expensive.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Purchase(player, disabledId, Guid.NewGuid())).StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.CoinTransactions.CountAsync(x => x.IdempotencyKey == paidKey.ToString("D")));
            Assert.Equal(0, await db.CoinTransactions.CountAsync(x => x.IdempotencyKey == freeKey.ToString("D")));
        }

        var admin = await Register(_factory.DevelopmentAdminEmail);
        var adminMe = await admin.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        Assert.Equal("admin", adminMe!.Role);
        var changed = await admin.PatchAsJsonAsync($"/api/v1/admin/heroes/{paid.Id}/price", new UpdatePriceRequest("125", "integration price test"), Json);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var audit = await admin.GetFromJsonAsync<AdminAuditPageDto>("/api/v1/admin/audit?limit=10", Json);
        Assert.Contains(audit!.Items, x => x.EntityId == paid.Id.ToString("D") && x.Reason == "integration price test");
        var refreshed = await admin.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json);
        var refreshedHero = refreshed!.Heroes.Single(x => x.Id == paid.Id);
        Assert.Equal("125", refreshedHero.CoinPrice); Assert.Equal(setupPoints, refreshedHero.SetupPoints);

        var playerCatalog = await player.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json);
        var byClass = playerCatalog!.Heroes.Where(x => x.IsOwned).GroupBy(x => x.ClassCode).ToDictionary(x => x.Key,
            x => new Queue<HeroDto>(x.OrderBy(h => !h.Code.StartsWith("dev-slot-", StringComparison.Ordinal)).ThenBy(h => h.Code)));
        var entries = playerCatalog.Slots.OrderBy(x => x.SlotNo)
            .Select(x => new LineupEntryInput(x.SlotNo, byClass[x.ClassCode].Dequeue().Id, null)).ToArray();
        var soldierIndex = Array.FindIndex(entries, x => playerCatalog.Slots.Single(s => s.SlotNo == x.SlotNo).ClassCode == "SOLDIER");
        entries[soldierIndex] = entries[soldierIndex] with { HeroId = paid.Id };
        var lineup = await Post<LineupDto>(player, "/api/v1/lineups", new SaveLineupRequest("Price snapshot", playerCatalog.Ruleset.Id, entries,
            playerCatalog.TeamSkills.Take(3).Select((x, i) => new LineupSkillInput(i + 1, x.Id)).ToArray()));
        var ticket = await Post<MatchmakingTicketDto>(player, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        await Put<MatchSelectionDto>(player, $"/api/v1/matches/{ticket.MatchId}/selection", new SelectLineupRequest(lineup.Id, lineup.Revision));
        await player.PostAsync($"/api/v1/matches/{ticket.MatchId}/confirm", null);
        var state = await player.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{ticket.MatchId}/state", Json);
        Assert.Equal(setupPoints, state!.State.GetProperty("pieces").EnumerateArray().Single(x => x.GetProperty("heroId").GetGuid() == paid.Id && x.GetProperty("side").GetString() == "red").GetProperty("setupPoints").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/v1/admin/heroes/{paid.Id}/price", new UpdatePriceRequest("333", "snapshot immutability"), Json)).StatusCode);
        await Post<MatchCommandResultDto>(player, $"/api/v1/matches/{ticket.MatchId}/commands", new MatchCommandRequest(Guid.NewGuid(), state.Version,
            JsonSerializer.SerializeToElement(new { type = "resign" }, Json)));
        var replay = await player.GetFromJsonAsync<ReplayPageDto>($"/api/v1/matches/{ticket.MatchId}/replay?afterSequence=-1&limit=10", Json);
        var startPiece = replay!.Entries.Single(x => x.SequenceNo == 0).StateAfter.GetProperty("pieces").EnumerateArray().Single(x => x.GetProperty("heroId").GetGuid() == paid.Id && x.GetProperty("side").GetString() == "red");
        Assert.Equal(setupPoints, startPiece.GetProperty("setupPoints").GetInt32());
    }

    // Register: Tạo tài khoản và gắn bearer token vào HttpClient.
    private async Task<HttpClient> Register(string? requestedEmail = null)
    {
        var client = _factory.CreateClient(); var email = requestedEmail ?? $"shop-{Guid.NewGuid():N}@hero.local"; var password = $"A1!{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password })).StatusCode);
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false", new { email, password })).Content.ReadFromJsonAsync<TokenResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken); return client;
    }

    // Purchase: POST mua hero kèm Idempotency-Key.
    private static Task<HttpResponseMessage> Purchase(HttpClient client, Guid heroId, Guid key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/shop/heroes/{heroId}/purchase");
        request.Headers.Add("Idempotency-Key", key.ToString("D")); return client.SendAsync(request);
    }
    // Post: POST JSON và đọc kết quả T sau khi assert success.
    private static async Task<T> Post<T>(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body, Json); var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}"); return JsonSerializer.Deserialize<T>(text, Json)!;
    }
    // Put: PUT JSON và đọc kết quả T sau khi assert success.
    private static async Task<T> Put<T>(HttpClient client, string path, object body)
    {
        var response = await client.PutAsJsonAsync(path, body, Json); var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}"); return JsonSerializer.Deserialize<T>(text, Json)!;
    }
    private sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);
}

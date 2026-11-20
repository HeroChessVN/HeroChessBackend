using System.Net;
using System.Net.Http.Json;
using HeroChess.Api.Data;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class ShopAdminTests
{
    [Fact]
    public async Task Readiness_rejects_a_missing_piece_loadout_table()
    {
        var client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE hero_chess.player_piece_loadout RENAME TO missing_piece_loadout_test");
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode); }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE hero_chess.missing_piece_loadout_test RENAME TO player_piece_loadout"); }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Piece_sets_are_account_scoped_and_concurrent_retries_charge_once()
    {
        var player = await Register();
        var other = await Register();
        var catalog = (await player.GetFromJsonAsync<PieceCosmeticCatalogDto>("/api/v1/piece-cosmetics", Json))!;
        Assert.Equal(4, catalog.Items.Count);
        Assert.Equal("mac-dinh", catalog.SelectedCode);
        Assert.Single(catalog.Items, x => x.IsOwned);
        var item = catalog.Items.Single(x => x.Code == "son-son");
        Assert.Equal("200", item.CoinPrice);
        Assert.Equal("Board/XiangqiPiecesSkinSonSon", item.AssetKey);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await player.PutAsJsonAsync("/api/v1/me/piece-cosmetic", new EquipPieceCosmeticRequest(item.Code), Json)).StatusCode);
        var key = Guid.NewGuid();
        var responses = await Task.WhenAll(PurchasePiece(player, item.Id, key), PurchasePiece(player, item.Id, key));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var first = (await responses[0].Content.ReadFromJsonAsync<PurchasePieceCosmeticDto>(Json))!;
        Assert.Equal(first, await responses[1].Content.ReadFromJsonAsync<PurchasePieceCosmeticDto>(Json));
        Assert.Equal("800", first.CoinBalance);
        Assert.Equal("200", first.Charged);
        Assert.Equal(HttpStatusCode.Conflict, (await PurchasePiece(player, item.Id, Guid.NewGuid())).StatusCode);
        await Put<PieceCosmeticDto>(player, "/api/v1/me/piece-cosmetic", new EquipPieceCosmeticRequest(item.Code));
        Assert.Equal(item.Code, (await player.GetFromJsonAsync<PieceCosmeticCatalogDto>("/api/v1/piece-cosmetics", Json))!.SelectedCode);
        Assert.Equal("mac-dinh", (await other.GetFromJsonAsync<PieceCosmeticCatalogDto>("/api/v1/piece-cosmetics", Json))!.SelectedCode);
        var boards = (await player.GetFromJsonAsync<BoardCosmeticCatalogDto>("/api/v1/board-cosmetics", Json))!;
        Assert.Equal(HttpStatusCode.Conflict, (await PurchaseBoard(player, boards.Items.Single(x => x.Code == "son-son").Id, key)).StatusCode);
        var heroes = (await player.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json))!;
        Assert.Equal(HttpStatusCode.Conflict, (await Purchase(player, heroes.Heroes.Single(x => x.Code == "dev-free-soldier").Id, key)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsync($"/api/v1/shop/piece-cosmetics/{item.Id}/purchase", null)).StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ledger = await db.CoinTransactions.SingleAsync(x => x.IdempotencyKey == key.ToString("D"));
        Assert.Equal("piece_cosmetic_purchase", ledger.Kind);
        Assert.Equal(item.Id, ledger.PieceCosmeticId);
        Assert.Null(ledger.BoardCosmeticId);
    }

    [Fact]
    public async Task Free_piece_acquisition_reserves_keys_and_both_bootstraps_preserve_custom_prices()
    {
        var player = await Register();
        var catalog = (await player.GetFromJsonAsync<PieceCosmeticCatalogDto>("/api/v1/piece-cosmetics", Json))!;
        var item = catalog.Items.Single(x => x.Code == "su-thanh");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.PieceCosmetics.SingleAsync(x => x.Id == item.Id);
        try
        {
            row.CoinPrice = 2001;
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PurchasePiece(player, item.Id, Guid.NewGuid())).StatusCode);
            row.CoinPrice = 0;
            await db.SaveChangesAsync();
            var key = Guid.NewGuid();
            var bought = (await (await PurchasePiece(player, item.Id, key)).Content.ReadFromJsonAsync<PurchasePieceCosmeticDto>(Json))!;
            Assert.Equal("0", bought.Charged);
            Assert.Equal("1000", bought.CoinBalance);
            Assert.False(await db.CoinTransactions.AnyAsync(x => x.IdempotencyKey == key.ToString("D")));
            foreach (var name in new[] { "17_board_cosmetics.sql", "21_piece_cosmetics.sql", "21_piece_cosmetics.sql" })
                await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Database", name)));
            Assert.Equal(0, (await db.PieceCosmetics.AsNoTracking().SingleAsync(x => x.Id == item.Id)).CoinPrice);
            var boards = (await player.GetFromJsonAsync<BoardCosmeticCatalogDto>("/api/v1/board-cosmetics", Json))!;
            Assert.Equal(HttpStatusCode.Conflict, (await PurchaseBoard(player, boards.Items.Single(x => x.Code == "su-thanh").Id, key)).StatusCode);
            var hero = (await player.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json))!.Heroes.Single(x => x.Code == "dev-free-soldier");
            Assert.Equal(HttpStatusCode.Conflict, (await Purchase(player, hero.Id, key)).StatusCode);
            var heroKey = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.OK, (await Purchase(player, hero.Id, heroKey)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PurchasePiece(player, catalog.Items.Single(x => x.Code == "son-mai").Id, heroKey)).StatusCode);
        }
        finally { row.CoinPrice = 200; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Disabled_piece_selection_falls_back_and_new_requests_are_rejected()
    {
        var player = await Register();
        var catalog = (await player.GetFromJsonAsync<PieceCosmeticCatalogDto>("/api/v1/piece-cosmetics", Json))!;
        var item = catalog.Items.Single(x => x.Code == "son-mai");
        (await PurchasePiece(player, item.Id, Guid.NewGuid())).EnsureSuccessStatusCode();
        await Put<PieceCosmeticDto>(player, "/api/v1/me/piece-cosmetic", new EquipPieceCosmeticRequest(item.Code));
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.PieceCosmetics.SingleAsync(x => x.Id == item.Id);
        try
        {
            row.IsEnabled = false; await db.SaveChangesAsync();
            var refreshed = (await player.GetFromJsonAsync<PieceCosmeticCatalogDto>("/api/v1/piece-cosmetics", Json))!;
            Assert.Equal("mac-dinh", refreshed.SelectedCode);
            Assert.DoesNotContain(refreshed.Items, x => x.Id == item.Id);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PurchasePiece(await Register(), item.Id, Guid.NewGuid())).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await player.PutAsJsonAsync("/api/v1/me/piece-cosmetic", new EquipPieceCosmeticRequest(item.Code), Json)).StatusCode);
            await using var adminFactory = new HeroChessFactory();
            var admin = await new ShopAdminTests(adminFactory).Register(adminFactory.DevelopmentAdminEmail);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/piece-cosmetics")).StatusCode);
        }
        finally { row.IsEnabled = true; await db.SaveChangesAsync(); }
    }

    private static Task<HttpResponseMessage> PurchasePiece(HttpClient client, Guid id, Guid key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/shop/piece-cosmetics/{id}/purchase");
        request.Headers.Add("Idempotency-Key", key.ToString("D"));
        return client.SendAsync(request);
    }
}

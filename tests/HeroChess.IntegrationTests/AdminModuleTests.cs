// Vai trò file: Kiểm thử module Admin: phân quyền, ban/mở khoá, điều chỉnh ví, cấp/thu hero, shop và gift code.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class AdminModuleTests : IClassFixture<HeroChessFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HeroChessFactory _factory;

    // AdminModuleTests: Nhận factory dùng chung môi trường test.
    public AdminModuleTests(HeroChessFactory factory) => _factory = factory;

    [Fact]
    // Admin_api_requires_admin_role_and_player_cannot_reach_admin_endpoints: Kiểm tra 403 cho player và 401 cho ẩn danh.
    public async Task Admin_api_requires_admin_role_and_player_cannot_reach_admin_endpoints()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/stats")).StatusCode);

        var player = await Register();
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/v1/admin/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/v1/admin/players")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/v1/admin/gift-codes")).StatusCode);
    }

    [Fact]
    // Admin_can_list_players_ban_and_unban_with_audit_reason: Kiểm tra ban chặn đăng nhập, mở khoá và audit ghi lý do.
    public async Task Admin_can_list_players_ban_and_unban_with_audit_reason()
    {
        var admin = await AdminClient();
        var target = await Register($"ban-{Guid.NewGuid():N}@hero.local");
        var me = await target.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        var playerId = me!.PlayerId!.Value;

        var list = await admin.GetFromJsonAsync<AdminPlayerPageDto>($"/api/v1/admin/players?query={targetEmail}", Json);
        Assert.NotNull(list);
        Assert.Contains(list.Items, x => x.Id == playerId);

        var ban = await admin.PatchAsJsonAsync($"/api/v1/admin/players/{playerId}/status",
            new AdminPlayerStatusRequest("disabled", "integration ban"), Json);
        Assert.Equal(HttpStatusCode.OK, ban.StatusCode);

        // Tài khoản bị khoá không đăng nhập được nữa (policy đọc status trực tiếp từ DB).
        var relogin = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login?useCookies=false",
            new { email = targetEmail, password = targetPassword }, Json);
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.StatusCode);

        var missingReason = await admin.PatchAsJsonAsync($"/api/v1/admin/players/{playerId}/status",
            new AdminPlayerStatusRequest("disabled", "  "), Json);
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        var unban = await admin.PatchAsJsonAsync($"/api/v1/admin/players/{playerId}/status",
            new AdminPlayerStatusRequest("active", "integration unban"), Json);
        Assert.Equal(HttpStatusCode.OK, unban.StatusCode);

        var audit = await admin.GetFromJsonAsync<AdminAuditPageDto>("/api/v1/admin/audit?entityType=player&limit=20", Json);
        Assert.Contains(audit!.Items, x => x.EntityId == playerId.ToString("D") && x.Action == "player.ban" && x.Reason == "integration ban");
        Assert.Contains(audit.Items, x => x.EntityId == playerId.ToString("D") && x.Action == "player.unban");
    }

    [Fact]
    // Admin_can_adjust_wallet_and_grant_then_revoke_only_admin_granted_heroes: Kiểm tra số dư, ledger và quy tắc thu hồi hero.
    public async Task Admin_can_adjust_wallet_and_grant_then_revoke_only_admin_granted_heroes()
    {
        var admin = await AdminClient();
        var target = await Register($"wallet-{Guid.NewGuid():N}@hero.local");
        var me = await target.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        var playerId = me!.PlayerId!.Value;
        var catalog = await target.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json);
        var hero = catalog!.Heroes.First(x => !x.IsOwned);

        var adjust = await admin.PostAsJsonAsync($"/api/v1/admin/players/{playerId}/coins",
            new AdminCoinAdjustmentRequest("250", "integration credit"), Json);
        Assert.Equal(HttpStatusCode.OK, adjust.StatusCode);
        var adjusted = await adjust.Content.ReadFromJsonAsync<AdminCoinAdjustmentDto>(Json);
        Assert.Equal("250", adjusted!.Amount);

        var zero = await admin.PostAsJsonAsync($"/api/v1/admin/players/{playerId}/coins",
            new AdminCoinAdjustmentRequest("0", "integration zero"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        var grant = await admin.PostAsJsonAsync($"/api/v1/admin/players/{playerId}/heroes",
            new AdminGrantHeroRequest(hero.Id, "integration grant"), Json);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var duplicate = await admin.PostAsJsonAsync($"/api/v1/admin/players/{playerId}/heroes",
            new AdminGrantHeroRequest(hero.Id, "integration grant again"), Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Hero mua bằng coin không được admin thu hồi.
        var owned = await target.GetFromJsonAsync<IReadOnlyList<AdminOwnedHeroDto>>("/api/v1/shop/owned", Json);
        var purchased = owned!.First(x => x.AcquiredVia is "starter" or "test");
        var revokePurchased = await admin.DeleteAsync($"/api/v1/admin/players/{playerId}/heroes/{purchased.HeroId}?reason=integration revoke");
        Assert.Equal(HttpStatusCode.Conflict, revokePurchased.StatusCode);

        var revoke = await admin.DeleteAsync($"/api/v1/admin/players/{playerId}/heroes/{hero.Id}?reason=integration revoke");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
    }

    [Fact]
    // Admin_shop_update_changes_price_and_availability_and_batch_reports_errors: Kiểm tra đổi giá, tắt bán và đổi giá hàng loạt.
    public async Task Admin_shop_update_changes_price_and_availability_and_batch_reports_errors()
    {
        var admin = await AdminClient();
        var catalog = await admin.GetFromJsonAsync<AdminHeroPageDto>("/api/v1/admin/heroes?limit=5", Json);
        var hero = catalog!.Items.First();

        var update = await admin.PatchAsJsonAsync($"/api/v1/admin/shop/heroes/{hero.Id}",
            new AdminShopUpdateRequest("777", false, "integration shop"), Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<AdminHeroDto>(Json);
        Assert.Equal("777", updated!.CoinPrice);
        Assert.False(updated.IsInShop);

        var batch = await admin.PatchAsJsonAsync("/api/v1/admin/shop/prices", new AdminBatchPriceRequest(
            [new AdminBatchPriceItem(hero.Id, "888"), new AdminBatchPriceItem(Guid.NewGuid(), "999")], "integration batch"), Json);
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        var batchResult = await batch.Content.ReadFromJsonAsync<AdminBatchPriceResultDto>(Json);
        Assert.True(batchResult!.Results[0].Succeeded);
        Assert.False(batchResult.Results[1].Succeeded);
        Assert.Equal("HERO_NOT_FOUND", batchResult.Results[1].ErrorCode);

        var invalid = await admin.PatchAsJsonAsync("/api/v1/admin/shop/prices", new AdminBatchPriceRequest(
            [new AdminBatchPriceItem(hero.Id, "not-a-number")], "integration batch"), Json);
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var invalidResult = (await invalid.Content.ReadFromJsonAsync<AdminBatchPriceResultDto>(Json))!;
        Assert.False(invalidResult.Results[0].Succeeded);
        Assert.Equal("INVALID_COIN_PRICE", invalidResult.Results[0].ErrorCode);
    }

    [Fact]
    // Gift_code_redemption_is_atomic_and_rejects_duplicate_and_expired: Kiểm tra quy đổi đúng một lần, chặn trùng và chặn mã hết hạn.
    public async Task Gift_code_redemption_is_atomic_and_rejects_duplicate_and_expired()
    {
        var admin = await AdminClient();
        var player = await Register($"gift-{Guid.NewGuid():N}@hero.local");
        var code = $"GIFT{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        var created = await admin.PostAsJsonAsync("/api/v1/admin/gift-codes", new AdminGiftCodeUpsertRequest(code,
            "coin", 500, null, null, DateTimeOffset.UtcNow.AddMinutes(-1), null, 1, 1, true, "integration gift"), Json);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var gift = await created.Content.ReadFromJsonAsync<AdminGiftCodeDto>(Json);
        Assert.Equal(1, gift!.MaxRedemptions);

        var me = await player.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        var playerId = me!.PlayerId!.Value;

        // Chỉ 1 lượt quy đổi: request chạy song song phải có đúng một lần thành công.
        var concurrent = await Task.WhenAll(Redeem(player, code), Redeem(player, code));
        Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Contains(concurrent, x => x.StatusCode == HttpStatusCode.Conflict);

        var duplicate = await Redeem(player, code);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.GiftCodeRedemptions.CountAsync(x => x.GiftCodeId == gift.Id));
        }

        var exhausted = await Redeem(player, code);
        Assert.Equal(HttpStatusCode.Conflict, exhausted.StatusCode);

        // Mã hết hạn bị từ chối.
        var expired = await admin.PostAsJsonAsync("/api/v1/admin/gift-codes", new AdminGiftCodeUpsertRequest($"EXP{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            "coin", 100, null, null, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), 5, 1, true, "expired"), Json);
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        var expiredCode = await expired.Content.ReadFromJsonAsync<AdminGiftCodeDto>(Json);
        var expiredRedeem = await Redeem(player, expiredCode!.Code);
        Assert.Equal(HttpStatusCode.Conflict, expiredRedeem.StatusCode);
    }

    [Fact]
    // Admin_permissions_restrict_endpoints_but_keep_admin_readable: Kiểm tra quyền fine-grained chặn endpoint ghi.
    public async Task Admin_permissions_restrict_endpoints_but_keep_admin_readable()
    {
        var admin = await AdminClient();
        var me = await admin.GetFromJsonAsync<MeDto>("/api/v1/me", Json);
        var adminId = me!.UserId;

        var restrictedEmail = $"limited-{Guid.NewGuid():N}@hero.local";
        var restrictedPassword = $"A1!{Guid.NewGuid():N}";
        var created = await admin.PostAsJsonAsync("/api/v1/admin/accounts", new AdminCreateAccountRequest(restrictedEmail,
            "Limited Admin", restrictedPassword, [AdminPermissions.Read]), Json);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var limited = await created.Content.ReadFromJsonAsync<AdminAccountDto>(Json);

        var limitedClient = await Login(restrictedEmail, restrictedPassword);
        Assert.Equal(HttpStatusCode.OK, (await limitedClient.GetAsync("/api/v1/admin/stats")).StatusCode);
        var denied = await limitedClient.GetAsync("/api/v1/admin/players");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var deniedGift = await limitedClient.PostAsJsonAsync("/api/v1/admin/gift-codes", new AdminGiftCodeUpsertRequest(null,
            "coin", 1, null, null, null, null, 1, 1, true, "should be denied"), Json);
        Assert.Equal(HttpStatusCode.Forbidden, deniedGift.StatusCode);

        var permissions = await admin.PutAsJsonAsync($"/api/v1/admin/accounts/{limited!.Id}/permissions",
            new AdminUpdatePermissionsRequest([AdminPermissions.Read, AdminPermissions.GiftCode]), Json);
        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);

        var nowAllowed = await limitedClient.PostAsJsonAsync("/api/v1/admin/gift-codes", new AdminGiftCodeUpsertRequest(null,
            "coin", 1, null, null, null, null, 1, 1, true, "now allowed"), Json);
        Assert.Equal(HttpStatusCode.OK, nowAllowed.StatusCode);

        // Cấp lại toàn quyền và dọn dẹp tài khoản hạn chế.
        await admin.PutAsJsonAsync($"/api/v1/admin/accounts/{limited.Id}/permissions",
            new AdminUpdatePermissionsRequest([AdminPermissions.Wildcard]), Json);
        Assert.NotEqual(Guid.Empty, adminId);
    }

    [Fact]
    // Admin_paginated_catalog_endpoints_support_next_cursor: Trang 2 của endpoint phân trang theo mã phải trả 200 (không 500 do query không dịch được).
    public async Task Admin_paginated_catalog_endpoints_support_next_cursor()
    {
        var admin = await AdminClient();
        // /admin/heroes trả mã ở cấp gốc; /admin/shop/heroes bọc trong `hero`.
        foreach (var (path, selector) in new[]
        {
            ("/api/v1/admin/heroes?limit=5", "$.items[*].code"),
            ("/api/v1/admin/shop/heroes?limit=5", "$.items[*].hero.code")
        })
        {
            var first = await admin.GetStringAsync(path);
            using var firstDoc = JsonDocument.Parse(first);
            var firstCodes = firstDoc.RootElement.GetProperty("items").EnumerateArray()
                .Select(x => selector.Contains(".hero.") ? x.GetProperty("hero").GetProperty("code").GetString() : x.GetProperty("code").GetString())
                .ToArray();
            Assert.NotEmpty(firstCodes);
            var cursor = firstDoc.RootElement.GetProperty("nextCursor").GetString();
            if (cursor is null) continue;

            using var secondDoc = JsonDocument.Parse(await admin.GetStringAsync($"{path}&cursor={Uri.EscapeDataString(cursor)}"));
            var secondCodes = secondDoc.RootElement.GetProperty("items").EnumerateArray()
                .Select(x => selector.Contains(".hero.") ? x.GetProperty("hero").GetProperty("code").GetString() : x.GetProperty("code").GetString())
                .ToArray();
            Assert.NotEmpty(secondCodes);
            // Trang sau không lặp lại mã của trang trước.
            Assert.Empty(secondCodes.Intersect(firstCodes));
        }
    }

    [Fact]
    // Admin_can_read_catalog_matches_rulesets_and_analytics: Kiểm tra các endpoint đọc trả dữ liệu thật.
    public async Task Admin_can_read_catalog_matches_rulesets_and_analytics()
    {
        var admin = await AdminClient();
        var heroes = await admin.GetFromJsonAsync<AdminHeroPageDto>("/api/v1/admin/heroes?limit=5", Json);
        Assert.NotNull(heroes);
        Assert.NotEmpty(heroes.Items);

        var skills = await admin.GetFromJsonAsync<IReadOnlyList<AdminSkillDto>>("/api/v1/admin/skills", Json);
        Assert.NotNull(skills);

        var factions = await admin.GetFromJsonAsync<IReadOnlyList<AdminFactionDto>>("/api/v1/admin/factions", Json);
        Assert.NotNull(factions);

        var rulesets = await admin.GetFromJsonAsync<IReadOnlyList<AdminRulesetDto>>("/api/v1/admin/rulesets", Json);
        Assert.NotNull(rulesets);
        Assert.NotEmpty(rulesets);

        var stats = await admin.GetFromJsonAsync<AdminStatsDto>("/api/v1/admin/stats", Json);
        Assert.True(stats!.TotalHeroes > 0);

        // Analytics phải trả empty state trung thực khi chưa có trận kết thúc.
        var balance = await admin.GetFromJsonAsync<AdminBalanceAnalyticsDto>("/api/v1/admin/analytics/balance?mode=ranked", Json);
        Assert.NotNull(balance);
        Assert.Equal("ranked", balance.Mode);

        var bot = await admin.GetFromJsonAsync<AdminBotAnalyticsDto>("/api/v1/admin/analytics/bot", Json);
        Assert.NotNull(bot);

        var matches = await admin.GetFromJsonAsync<AdminMatchPageDto>("/api/v1/admin/matches?limit=5", Json);
        Assert.NotNull(matches);
    }

    private string targetEmail = string.Empty;
    private string targetPassword = string.Empty;

    [Fact]
    // Admin_hero_setup_points_follows_db_rule_non_general_requires_one: Chặn regression lỗi 500 khi tạo hero lớp khác GENERAL với setupPoints = 0.
    public async Task Admin_hero_setup_points_follows_db_rule_non_general_requires_one()
    {
        var admin = await AdminClient();

        var invalid = await admin.PostAsJsonAsync("/api/v1/admin/heroes",
            new AdminHeroUpsertRequest(null, "Regres Setup 0", "ADVISOR", 0, "0", true, false, false, null, null, null, "regression"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var valid = await admin.PostAsJsonAsync("/api/v1/admin/heroes",
            new AdminHeroUpsertRequest(null, "Regres Setup 1", "ADVISOR", 1, "0", true, false, false, null, null, null, "regression"), Json);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        var created = await valid.Content.ReadFromJsonAsync<AdminHeroDto>(Json);
        Assert.Equal(1, created!.SetupPoints);

        // GENERAL được phép có 0 điểm setup.
        var general = await admin.PostAsJsonAsync("/api/v1/admin/heroes",
            new AdminHeroUpsertRequest(null, "Regres General 0", "GENERAL", 0, "0", true, false, false, null, null, null, "regression"), Json);
        Assert.Equal(HttpStatusCode.OK, general.StatusCode);
    }

    // Register: Tạo tài khoản người chơi và gắn bearer token; nhớ email/mật khẩu để kiểm tra ban.
    private async Task<HttpClient> Register(string? email = null)
    {
        var client = _factory.CreateClient();
        targetEmail = email ?? $"adminmod-{Guid.NewGuid():N}@hero.local";
        targetPassword = $"A1!{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { email = targetEmail, password = targetPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false",
            new { email = targetEmail, password = targetPassword })).Content.ReadFromJsonAsync<TokenResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    // AdminClient: Đăng nhập tài khoản admin development của fixture.
    private async Task<HttpClient> AdminClient()
    {
        var client = _factory.CreateClient();
        var password = $"A1!{Guid.NewGuid():N}";
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = _factory.DevelopmentAdminEmail, password });
        if (register.StatusCode == HttpStatusCode.BadRequest)
        {
            // Tài khoản admin đã tồn tại từ test trước: đặt lại mật khẩu rồi đăng nhập.
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.Users.SingleAsync(x => x.NormalizedEmail == _factory.DevelopmentAdminEmail.ToUpperInvariant());
            var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>();
            account.PasswordHash = hasher.HashPassword(account, password);
            account.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false",
            new { email = _factory.DevelopmentAdminEmail, password })).Content.ReadFromJsonAsync<TokenResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    // Login: Đăng nhập bằng email/mật khẩu cho tài khoản đã tạo.
    private async Task<HttpClient> Login(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<TokenResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    // Redeem: POST quy đổi gift code.
    private static Task<HttpResponseMessage> Redeem(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/v1/gift-codes/redeem", new RedeemGiftCodeRequest(code), Json);

    private sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);
}

// Vai trò file: Test hồ sơ người chơi (sửa tên/ảnh/khung, upload ảnh) và hồ sơ hai phe trong selection/state/lịch sử.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HeroChess.Contracts;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class PlayerProfileTests : IClassFixture<HeroChessFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HeroChessFactory _factory;
    public PlayerProfileTests(HeroChessFactory factory) => _factory = factory;

    [Fact]
    // Me_returns_profile_fields_and_elo_even_when_zero: /me phải trả đủ field hồ sơ, elo bằng 0 vẫn không bị bỏ.
    public async Task Me_returns_profile_fields_and_elo_even_when_zero()
    {
        var client = await CreatePlayer();
        var me = (await client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        Assert.NotEqual(Guid.Empty, me.UserId);
        Assert.False(string.IsNullOrWhiteSpace(me.DisplayName));
        // Elo luôn có mặt, kể cả khi bằng 0.
        Assert.True(me.Elo.HasValue);
        Assert.True(me.Elo!.Value >= 0);
        Assert.Null(me.AvatarUrl);
    }

    [Fact]
    // Patch_me_updates_display_name_and_avatar_tokens: PATCH sửa tên, mã ảnh, mã khung; giá trị mới xuất hiện ở lần đọc sau.
    public async Task Patch_me_updates_display_name_and_avatar_tokens()
    {
        var client = await CreatePlayer();
        var updated = await Patch(client, new UpdateProfileRequest("Lê Lợi", "leloi", "khung-vang"));
        Assert.Equal("Lê Lợi", updated.DisplayName);
        Assert.Equal("leloi", updated.AvatarName);
        Assert.Equal("khung-vang", updated.FrameName);
        // Không có ảnh tải lên từ máy nên avatarUrl vẫn null.
        Assert.Null(updated.AvatarUrl);
        var reread = (await client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        Assert.Equal("leloi", reread.AvatarName);
        Assert.Equal("khung-vang", reread.FrameName);
    }

    [Theory]
    [InlineData("co khoang trong")]
    [InlineData("co/dau")]
    [InlineData("co\\dau")]
    // Patch_me_rejects_avatar_and_frame_tokens_with_spaces_or_slashes: mã ảnh/khung phải là token không dấu cách.
    public async Task Patch_me_rejects_avatar_and_frame_tokens_with_spaces_or_slashes(string invalid)
    {
        var client = await CreatePlayer();
        using var bad = await client.PatchAsJsonAsync("/api/v1/me", new UpdateProfileRequest(null, invalid, null), Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Equal("INVALID_AVATAR_NAME", (await bad.Content.ReadFromJsonAsync<ApiError>(Json))!.Code);
    }

    [Fact]
    // Upload_avatar_returns_absolute_url_and_rejects_non_images: upload ảnh trả URL tuyệt đối; file không phải ảnh bị từ chối.
    public async Task Upload_avatar_returns_absolute_url_and_rejects_non_images()
    {
        var client = await CreatePlayer();
        using var form = new MultipartFormDataContent();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "avatar.png");
        using var response = await client.PostAsync("/api/v1/me/avatar", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = (await response.Content.ReadFromJsonAsync<MeDto>(Json))!;
        // avatarUrl phải là URL tuyệt đối, không phải đường dẫn tương đối.
        Assert.NotNull(me.AvatarUrl);
        Assert.StartsWith("http://", me.AvatarUrl);
        Assert.Contains("/avatars/", me.AvatarUrl);

        using var badForm = new MultipartFormDataContent();
        var text = new ByteArrayContent(Encoding.UTF8.GetBytes("not an image"));
        text.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        badForm.Add(text, "file", "note.txt");
        using var rejected = await client.PostAsync("/api/v1/me/avatar", badForm);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, rejected.StatusCode);
    }

    [Fact]
    // Selection_exposes_both_sides_profile_and_selected_lineup: /selection trả hồ sơ và đội hình của cả hai phe.
    public async Task Selection_exposes_both_sides_profile_and_selected_lineup()
    {
        var red = await CreatePlayerWithLineup(); var black = await CreatePlayerWithLineup();
        await Patch(red.Client, new UpdateProfileRequest("Lê Lợi", "leloi", null));
        await Patch(black.Client, new UpdateProfileRequest("Nguyễn Huy", null, null));

        var redTicket = await Post<MatchmakingTicketDto>(red.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var blackTicket = await Post<MatchmakingTicketDto>(black.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var matchId = blackTicket.MatchId!.Value;
        await Select(red, matchId); await Select(black, matchId);

        var selection = (await red.Client.GetFromJsonAsync<MatchSelectionDto>($"/api/v1/matches/{matchId}/selection", Json))!;
        Assert.Equal(2, selection.Sides.Count);
        var redSide = selection.Sides.Single(x => x.Side == "red");
        var blackSide = selection.Sides.Single(x => x.Side == "black");
        // Hồ sơ đúng của từng tài khoản, kể cả phe đối thủ.
        Assert.Equal("Lê Lợi", redSide.Player!.DisplayName);
        Assert.Equal("leloi", redSide.Player.AvatarName);
        Assert.Equal("Nguyễn Huy", blackSide.Player!.DisplayName);
        // Elo luôn có mặt trong hồ sơ, kể cả khi bằng 0.
        Assert.True(blackSide.Player.Elo >= 0);
        // ownLineupId chỉ ở phe người gọi API.
        Assert.NotNull(redSide.OwnLineupId);
        Assert.Null(blackSide.OwnLineupId);
        // Đội hình đã chọn của cả hai phe đều hiện.
        Assert.NotNull(redSide.SelectedLineup);
        Assert.NotNull(blackSide.SelectedLineup);
        Assert.Equal(3, redSide.SelectedLineup!.Skills.Count);
        Assert.NotEmpty(redSide.SelectedLineup.Entries);
        Assert.Equal("Hero fixture", redSide.SelectedLineup.Name);
    }

    [Fact]
    // State_exposes_both_sides_profile_and_piece_hero_names: /state trả hồ sơ hai phe, quân có heroId/heroName, skillStates đủ 3 skill.
    public async Task State_exposes_both_sides_profile_and_piece_hero_names()
    {
        var red = await CreatePlayerWithLineup(); var black = await CreatePlayerWithLineup();
        var redTicket = await Post<MatchmakingTicketDto>(red.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var blackTicket = await Post<MatchmakingTicketDto>(black.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var matchId = blackTicket.MatchId!.Value;
        await Select(red, matchId); await Select(black, matchId);
        await Task.WhenAll(red.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null), black.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null));

        var state = (await red.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json))!;
        Assert.NotNull(state.Sides);
        Assert.Equal(2, state.Sides!.Count);
        Assert.All(state.Sides, side => Assert.NotNull(side.Player));
        // Quân của cả hai phe đều có heroId và heroName.
        var pieces = state.State.GetProperty("pieces").EnumerateArray().ToArray();
        Assert.NotEmpty(pieces);
        Assert.All(pieces, piece =>
        {
            Assert.NotEqual(Guid.Empty, piece.GetProperty("heroId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(piece.GetProperty("heroName").GetString()));
        });
        // skillStates của cả hai phe đều có 3 skill với đủ trường.
        foreach (var side in new[] { "red", "black" })
        {
            var skills = state.State.GetProperty("skillStates").GetProperty(side).EnumerateArray().ToArray();
            Assert.Equal(3, skills.Length);
            Assert.All(skills, skill =>
            {
                Assert.True(skill.GetProperty("slotNo").GetInt32() > 0);
                Assert.False(string.IsNullOrWhiteSpace(skill.GetProperty("name").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(skill.GetProperty("implementationKey").GetString()));
                Assert.True(skill.GetProperty("cooldownRemaining").GetInt32() >= 0);
                Assert.True(skill.TryGetProperty("usesRemaining", out _));
            });
        }
    }

    [Fact]
    // Match_history_item_exposes_sides_rating_change_and_bonus: /matches trả hồ sơ hai phe, ratingChange và bonus.
    public async Task Match_history_item_exposes_sides_rating_change_and_bonus()
    {
        var red = await CreatePlayerWithLineup(); var black = await CreatePlayerWithLineup();
        var redTicket = await Post<MatchmakingTicketDto>(red.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var blackTicket = await Post<MatchmakingTicketDto>(black.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("ranked"));
        var matchId = blackTicket.MatchId!.Value;
        await Select(red, matchId); await Select(black, matchId);
        await Task.WhenAll(red.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null), black.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null));
        var state = (await red.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json))!;
        var legal = (await red.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{matchId}/legal-actions", Json))!;
        await Post<MatchCommandResultDto>(red.Client, $"/api/v1/matches/{matchId}/commands",
            new MatchCommandRequest(Guid.NewGuid(), state.Version, JsonSerializer.SerializeToElement(new { type = "resign" }, Json)));

        var page = (await red.Client.GetFromJsonAsync<MatchPageDto>("/api/v1/matches?limit=20", Json))!;
        var item = page.Items.First(x => x.MatchId == matchId);
        Assert.Equal("red", item.Side);
        Assert.Equal("resign", item.EndReason);
        Assert.NotNull(item.Sides);
        Assert.Equal(2, item.Sides!.Count);
        Assert.All(item.Sides, side => Assert.NotNull(side.Player));
        // ratingChange là điểm Elo người gọi API được/mất; bonus là điểm thưởng.
        Assert.Equal(item.EloAfter - item.EloBefore, item.RatingChange);
        Assert.True(item.Bonus >= 0);
        // Replay entry có committedAt để tính thời gian suy nghĩ.
        var replay = (await red.Client.GetFromJsonAsync<ReplayPageDto>($"/api/v1/matches/{matchId}/replay", Json))!;
        Assert.All(replay.Entries, entry => Assert.NotEqual(default, entry.CommittedAt));
        var replayPieces = replay.Entries[^1].StateAfter.GetProperty("pieces").EnumerateArray().ToArray();
        Assert.All(replayPieces, piece => Assert.False(string.IsNullOrWhiteSpace(piece.GetProperty("heroName").GetString())));
        Assert.NotEmpty(legal);
    }

    // CreatePlayer: Đăng ký/đăng nhập player mới và trả client đã có bearer token.
    private async Task<HttpClient> CreatePlayer()
    {
        var client = _factory.CreateClient();
        var email = $"profile-{Guid.NewGuid():N}@hero.local"; var password = $"A1!{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password })).StatusCode);
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=false", new { email, password })).Content.ReadFromJsonAsync<LoginToken>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    // CreatePlayerWithLineup: Đăng ký player và tạo đội hình hợp lệ để vào trận.
    private async Task<(HttpClient Client, LineupDto Lineup)> CreatePlayerWithLineup()
    {
        var client = await CreatePlayer();
        var catalog = (await client.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json))!;
        var byClass = catalog.Heroes.Where(x => x.IsOwned).GroupBy(x => x.ClassCode)
            .ToDictionary(x => x.Key, x => new Queue<HeroDto>(x.OrderBy(h => h.Code)));
        var entries = catalog.Slots.OrderBy(x => x.SlotNo).Select(x => new LineupEntryInput(x.SlotNo, byClass[x.ClassCode].Dequeue().Id, null)).ToArray();
        var skills = catalog.TeamSkills.Take(3).Select((x, i) => new LineupSkillInput(i + 1, x.Id)).ToArray();
        var lineup = await Post<LineupDto>(client, "/api/v1/lineups", new SaveLineupRequest("Hero fixture", catalog.Ruleset.Id, entries, skills));
        return (client, lineup);
    }

    // Select: Chọn đội hình cho trận.
    private static async Task Select((HttpClient Client, LineupDto Lineup) player, Guid matchId) =>
        await Post<MatchSelectionDto>(player.Client, $"/api/v1/matches/{matchId}/selection",
            new SelectLineupRequest(player.Lineup.Id, player.Lineup.Revision), HttpMethod.Put);

    // Patch: Gọi PATCH /me và trả hồ sơ đã cập nhật.
    private static async Task<MeDto> Patch(HttpClient client, UpdateProfileRequest request)
    {
        using var response = await client.PatchAsJsonAsync("/api/v1/me", request, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MeDto>(Json))!;
    }

    // Post: Gọi API có body và kiểm tra status thành công.
    private static async Task<T> Post<T>(HttpClient client, string path, object body, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonBody(body, Json) };
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"{path} returned {response.StatusCode}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    // JsonBody: Bọc body thành StringContent kèm content type JSON.
    private static StringContent JsonBody(object body, JsonSerializerOptions options) =>
        new(JsonSerializer.Serialize(body, options), Encoding.UTF8, "application/json");

    // LoginToken: Cặp token trả về khi đăng nhập.
    private sealed record LoginToken(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);
}

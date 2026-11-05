using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Contracts;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class MatchFlowTests
{
    [Fact]
    public async Task Custom_room_keeps_its_code_after_transfer_and_closes_when_new_owner_ends_session()
    {
        const string rooms = "/api/v1/matchmaking/rooms";
        var owner = await CreatePlayerWithLineup();
        var guest = await CreatePlayerWithLineup();
        var outsider = await CreatePlayerWithLineup();
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/health/ready")).StatusCode);
        var room = await Post<CustomRoomDto>(owner.Client, rooms, new { });
        var path = $"{rooms}/{room.RoomId}";
        Assert.Equal(("waiting", 1, 2, true), (room.Status, room.MemberCount, room.Capacity, room.IsOwner));
        Assert.Equal(8, room.Code.Length);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync("/api/v1/matchmaking/tickets",
            new CreateMatchmakingTicketRequest("bot"), Json)).StatusCode);

        var joins = await Task.WhenAll(new[] { guest.Client, outsider.Client }.Select(async client =>
            (Client: client, Response: await client.PostAsJsonAsync($"{rooms}/join", new JoinCustomRoomRequest(room.Code.ToLowerInvariant()), Json))));
        var winner = Assert.Single(joins, x => x.Response.StatusCode == HttpStatusCode.OK);
        Assert.Single(joins, x => x.Response.StatusCode == HttpStatusCode.Conflict);
        var joined = (await winner.Response.Content.ReadFromJsonAsync<CustomRoomDto>(Json))!;
        Assert.Equal(("selecting", 2), (joined.Status, joined.MemberCount));
        Assert.Equal(joined.MatchId, (await Post<CustomRoomDto>(winner.Client, $"{rooms}/join", new JoinCustomRoomRequest(room.Code))).MatchId);
        Assert.Equal(HttpStatusCode.NoContent, (await winner.Client.PostAsJsonAsync($"{path}/leave",
            new LeaveCustomRoomRequest("leave_room"), Json)).StatusCode);
        var waiting = (await owner.Client.GetFromJsonAsync<CustomRoomDto>(path, Json))!;
        Assert.Equal((room.Code, "waiting", 1), (waiting.Code, waiting.Status, waiting.MemberCount));
        await Post<CustomRoomDto>(winner.Client, $"{rooms}/join", new JoinCustomRoomRequest(room.Code));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsJsonAsync($"{path}/leave",
            new LeaveCustomRoomRequest("leave_room"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.GetAsync($"{rooms}/current")).StatusCode);
        var transferred = (await winner.Client.GetFromJsonAsync<CustomRoomDto>($"{rooms}/current", Json))!;
        Assert.Equal((room.RoomId, room.Code, "waiting", 1, true), (transferred.RoomId, transferred.Code, transferred.Status, transferred.MemberCount, transferred.IsOwner));
        Assert.Equal(HttpStatusCode.NoContent, (await winner.Client.PostAsJsonAsync($"{path}/leave",
            new LeaveCustomRoomRequest("end_session"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsJsonAsync($"{rooms}/join",
            new JoinCustomRoomRequest(room.Code), Json)).StatusCode);
    }

    [Fact]
    public async Task Custom_match_leaves_ratings_and_coins_unchanged_and_rematch_is_idempotent()
    {
        const string rooms = "/api/v1/matchmaking/rooms";
        var host = await CreatePlayerWithLineup();
        var guest = await CreatePlayerWithLineup();
        var hostBefore = (await host.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        var guestBefore = (await guest.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        var ratingBefore = (await host.Client.GetFromJsonAsync<MyRankingDto>("/api/v1/ranking/me", Json))!;
        var room = await Post<CustomRoomDto>(host.Client, rooms, new { });
        var joined = await Post<CustomRoomDto>(guest.Client, $"{rooms}/join", new JoinCustomRoomRequest(room.Code));
        var matchId = joined.MatchId!.Value;
        foreach (var player in new[] { host, guest })
        {
            await Post<MatchSelectionDto>(player.Client, $"/api/v1/matches/{matchId}/selection",
                new SelectLineupRequest(player.Lineup.Id, player.Lineup.Revision), HttpMethod.Put);
            using var confirmed = await player.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null);
            confirmed.EnsureSuccessStatusCode();
        }
        await Post<MatchCommandResultDto>(guest.Client, $"/api/v1/matches/{matchId}/commands",
            new MatchCommandRequest(Guid.NewGuid(), 0, JsonSerializer.SerializeToElement(new { type = "resign" }, Json)));
        var hostAfter = (await host.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        var guestAfter = (await guest.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        Assert.Equal((hostBefore.Elo, hostBefore.CoinBalance), (hostAfter.Elo, hostAfter.CoinBalance));
        Assert.Equal((guestBefore.Elo, guestBefore.CoinBalance), (guestAfter.Elo, guestAfter.CoinBalance));
        Assert.Equal(ratingBefore, await host.Client.GetFromJsonAsync<MyRankingDto>("/api/v1/ranking/me", Json));

        var request = new RematchCustomRoomRequest(matchId, Guid.NewGuid());
        var path = $"{rooms}/{room.RoomId}/rematch";
        var rematch = await Post<CustomRoomDto>(host.Client, path, request);
        Assert.NotEqual(matchId, rematch.MatchId);
        Assert.Equal(room.Code, rematch.Code);
        Assert.Equal(rematch.MatchId, (await Post<CustomRoomDto>(host.Client, path, request)).MatchId);
        Assert.True((await guest.Client.DeleteAsync($"/api/v1/matches/{rematch.MatchId}/selection")).IsSuccessStatusCode);
        var delayedRetry = await Post<CustomRoomDto>(host.Client, path, request);
        Assert.Null(delayedRetry.MatchId);
        Assert.Equal(rematch.MatchId, delayedRetry.LastMatchId);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PostAsJsonAsync($"{rooms}/{room.RoomId}/leave",
            new LeaveCustomRoomRequest("end_session"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await guest.Client.GetAsync($"{rooms}/current")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.Client.PostAsJsonAsync($"{rooms}/join",
            new JoinCustomRoomRequest(room.Code), Json)).StatusCode);
    }
}

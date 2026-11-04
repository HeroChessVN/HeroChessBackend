using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class MatchFlowTests
{
    [Theory]
    [InlineData("easy")]
    [InlineData("medium")]
    [InlineData("hard")]
    public async Task Bot_level_persists_and_worker_plays_through_normal_pipeline(string difficulty)
    {
        var human = await CreatePlayerWithLineup();
        var ticket = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot", difficulty));
        Assert.Equal(difficulty, ticket.Bot!.Difficulty);
        var id = ticket.MatchId!.Value;
        await Select(human, id);
        var selection = (await human.Client.GetFromJsonAsync<MatchSelectionDto>($"/api/v1/matches/{id}/selection", Json))!;
        Assert.Equal(difficulty, selection.Sides.Single(s => s.IsBot).Bot!.Difficulty);
        (await human.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
        var state = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
        Assert.Equal(difficulty, state.Bot!.Difficulty);
        var legal = (await human.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{id}/legal-actions", Json))!;
        var ack = await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{id}/commands", Command(state.Version, legal[0]));
        Assert.Equal(difficulty, ack.Snapshot.Bot!.Difficulty);
        var until = DateTime.UtcNow.AddSeconds(10);
        do
        {
            await Task.Delay(25);
            state = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
        } while (state.Version <= ack.SequenceNo && DateTime.UtcNow < until);
        Assert.Equal(ack.SequenceNo + 1, state.Version);
        Assert.Equal("red", state.SideToMove);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var config = await db.MatchParticipants.AsNoTracking().SingleAsync(p => p.MatchId == id && p.ParticipantType == "bot");
            Assert.Equal(difficulty, BotConfiguration.Read(config.BotConfig).Difficulty);
            Assert.Equal(1, await db.MatchActions.CountAsync(a => a.MatchId == id && a.ActorSide == "black"));
        }
        await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{id}/commands", new MatchCommandRequest(Guid.NewGuid(), state.Version,
            JsonSerializer.SerializeToElement(new { type = "resign" }, Json)));
        var history = (await human.Client.GetFromJsonAsync<MatchDetailDto>($"/api/v1/matches/{id}", Json))!;
        Assert.Equal(difficulty, history.Sides.Single(s => s.IsBot).Bot!.Difficulty);
    }

    [Fact]
    public async Task Invalid_difficulty_does_not_leave_reservation_and_legacy_client_still_works()
    {
        var human = await CreatePlayerWithLineup();
        foreach (var input in new[] { new CreateMatchmakingTicketRequest("ranked", "easy"), new CreateMatchmakingTicketRequest("bot", "unknown"), new CreateMatchmakingTicketRequest(null!, "easy") })
            Assert.Equal(HttpStatusCode.BadRequest, (await human.Client.PostAsJsonAsync("/api/v1/matchmaking/tickets", input, Json)).StatusCode);
        var legacy = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        Assert.Null(legacy.Bot);
        (await human.Client.DeleteAsync($"/api/v1/matches/{legacy.MatchId}/selection")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Corrupt_bot_config_is_cancelled_once_and_client_cannot_cancel_active_match()
    {
        var human = await CreatePlayerWithLineup();
        var ticket = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot", "easy"));
        var id = ticket.MatchId!.Value;
        await Select(human, id);
        (await human.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await human.Client.PostAsJsonAsync($"/api/v1/matches/{id}/commands",
            new MatchCommandRequest(Guid.NewGuid(), 0, JsonSerializer.SerializeToElement(new { type = "cancel" }, Json)), Json)).StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participant = await db.MatchParticipants.SingleAsync(p => p.MatchId == id && p.ParticipantType == "bot");
        participant.BotConfig = GameJson.Document(new { schemaVersion = 999 });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<MatchCommandService>();
        Assert.True(await service.CancelBotFailureAsync(id, 0, CancellationToken.None));
        Assert.False(await service.CancelBotFailureAsync(id, 0, CancellationToken.None));
        db.ChangeTracker.Clear();
        var match = await db.Matches.SingleAsync(m => m.Id == id);
        Assert.Equal("cancelled", match.Status);
        Assert.NotNull(match.SettledAt);
        Assert.Equal(1, await db.MatchActions.CountAsync(a => a.MatchId == id && a.Kind == "cancel"));
        Assert.Equal(0, (await db.MatchStates.SingleAsync(s => s.MatchId == id)).CountedActions);
        var next = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot", "medium"));
        Assert.NotNull(next.MatchId);
        (await human.Client.DeleteAsync($"/api/v1/matches/{next.MatchId}/selection")).EnsureSuccessStatusCode();
    }
}

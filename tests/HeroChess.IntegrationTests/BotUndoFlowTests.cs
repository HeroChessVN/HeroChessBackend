using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using HeroChess.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class MatchFlowTests
{
    [Fact]
    public async Task Automatic_bot_undo_restores_the_latest_human_action_on_the_current_branch()
    {
        var scheduler = _factory.Services.GetRequiredService<BotTurnScheduler>();
        // Commit bot replies explicitly so both orderings of the bot/Undo race are deterministic.
        await scheduler.StopAsync(CancellationToken.None);
        try
        {
            var human = await CreatePlayerWithLineup();
            var catalog = (await human.Client.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json))!;
            var skill = catalog.TeamSkills.Single(x => x.ImplementationKey == "thanh");
            var lineup = await Post<LineupDto>(human.Client, $"/api/v1/lineups/{human.Lineup.Id}",
                new SaveLineupRequest(human.Lineup.Name, catalog.Ruleset.Id, human.Lineup.Entries,
                    [new LineupSkillInput(1, skill.Id), .. catalog.TeamSkills.Where(x => x.Id != skill.Id).Take(2)
                        .Select((x, index) => new LineupSkillInput(index + 2, x.Id))],
                    human.Lineup.Revision), HttpMethod.Put);
            var ticket = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
            var matchId = ticket.MatchId!.Value;
            await Select((human.Client, lineup), matchId);
            (await human.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null)).EnsureSuccessStatusCode();

            var commands = $"/api/v1/matches/{matchId}/commands";
            var initial = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json))!;
            await Reject(Undo(0), HttpStatusCode.UnprocessableEntity, "NO_UNDO_AVAILABLE");
            await Reject(Request(0, new { type = "undo", targetSequence = "0" }), HttpStatusCode.BadRequest, "INVALID_ACTION");
            await Reject(Request(0, new { type = "undo", targetSequence = 0 }), HttpStatusCode.UnprocessableEntity, "INVALID_UNDO_TARGET");

            var activated = await Send(Request(0, new { type = "team_skill", slot = 1, target = new { position = new { x = 4, y = 2 } } }));
            Assert.Equal(1, activated.SequenceNo);
            Assert.Equal(7, activated.Snapshot.State.GetProperty("skillStates").GetProperty("red")[0].GetProperty("cooldownRemaining").GetInt32());
            var afterSkillAndReply = (await Reply(await PrepareReply(1))).Snapshot;
            Assert.Equal(2, afterSkillAndReply.Version);

            Assert.Equal(3, (await Move(0)).SequenceNo);
            var queuedReply = await PrepareReply(3);
            var beforeBot = await Send(Undo(3));
            Restored(beforeBot, 4, 2, afterSkillAndReply);
            var staleBot = await Assert.ThrowsAsync<ApiException>(() => Reply(queuedReply));
            Assert.Equal("STALE_STATE", staleBot.Code);

            Assert.Equal(5, (await Move(1)).SequenceNo);
            Assert.Equal(6, (await Reply(await PrepareReply(5))).SequenceNo);
            await Reject(Undo(5), HttpStatusCode.Conflict, "STALE_STATE");
            var undo = Undo(6);
            Restored(await Send(undo), 7, 4, afterSkillAndReply);
            Restored(await Send(Undo(7)), 8, 0, initial);
            var duplicate = await Send(undo);
            Assert.True(duplicate.Duplicate);
            Assert.Equal(7, duplicate.SequenceNo);
            await Reject(Undo(8), HttpStatusCode.UnprocessableEntity, "NO_UNDO_AVAILABLE");

            var replay = (await human.Client.GetFromJsonAsync<ReplayPageDto>($"/api/v1/matches/{matchId}/replay?afterSequence=-1&limit=20", Json))!;
            Assert.Equal(new[] { "start", "team_skill", "move", "move", "undo", "move", "move", "undo", "undo" }, replay.Entries.Select(x => x.Kind));
            await Send(Request(8, new { type = "resign" }));
            await Reject(Undo(9), HttpStatusCode.Conflict, "MATCH_NOT_ACTIVE");

            Task<MatchCommandResultDto> Send(MatchCommandRequest request) => Post<MatchCommandResultDto>(human.Client, commands, request);

            async Task Reject(MatchCommandRequest request, HttpStatusCode status, string code)
            {
                using var response = await human.Client.PostAsJsonAsync(commands, request, Json);
                Assert.Equal(status, response.StatusCode);
                Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>(Json))!.Code);
            }

            async Task<MatchCommandResultDto> Move(int index)
            {
                var state = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json))!;
                var legal = (await human.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{matchId}/legal-actions", Json))!;
                return await Send(Command(state.Version, legal[index]));
            }

            async Task<MatchCommandRequest> PrepareReply(int version)
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var row = await db.MatchStates.AsNoTracking().SingleAsync(x => x.MatchId == matchId);
                Assert.Equal(version, row.Version);
                Assert.Equal("black", row.SideToMove);
                var move = new XiangqiRulesEngine().GenerateLegalActions(GameJson.Read<GameState>(row.State)).First();
                return Request(version, new { type = "move", pieceId = move.PieceId, to = new { x = move.To.X, y = move.To.Y } });
            }

            async Task<MatchCommandResultDto> Reply(MatchCommandRequest request)
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<MatchCommandService>()
                    .ExecuteBotAsync(matchId, request, null, CancellationToken.None);
            }
        }
        finally
        {
            await scheduler.StartAsync(CancellationToken.None);
        }

        static MatchCommandRequest Undo(int version) => Request(version, new { type = "undo" });
        static MatchCommandRequest Request(int version, object action) => new(Guid.NewGuid(), version, JsonSerializer.SerializeToElement(action, Json));

        static void Restored(MatchCommandResultDto result, int version, int target, MatchStateDto expected)
        {
            Assert.Equal(version, result.SequenceNo);
            Assert.Equal(version, result.Snapshot.Version);
            var undoEvent = Assert.Single(result.ResolvedEvents.EnumerateArray());
            Assert.Equal("match.undone", undoEvent.GetProperty("type").GetString());
            Assert.Equal(target, undoEvent.GetProperty("targetSequence").GetInt32());
            Assert.Equal(expected.SideToMove, result.Snapshot.SideToMove);
            Assert.Equal(expected.TurnIndex, result.Snapshot.TurnIndex);
            Assert.Equal(expected.CountedActions, result.Snapshot.CountedActions);
            Assert.True(result.Snapshot.DeadlineAt > result.Snapshot.ServerNow);
            foreach (var property in expected.State.EnumerateObject().Where(x => x.Name != "version"))
                Assert.True(JsonElement.DeepEquals(property.Value, result.Snapshot.State.GetProperty(property.Name)), property.Name);
        }
    }
}

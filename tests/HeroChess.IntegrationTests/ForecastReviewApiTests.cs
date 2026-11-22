using System.Net;
using System.Net.Http.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class MatchFlowTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Forecast_review_keeps_deadline_and_reconnect_state_then_finishes_once(int completion)
    {
        var (id, red, black) = await Ranked();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var participant = await db.MatchParticipants.SingleAsync(p => p.MatchId == id && p.Side == "red");
            var lineup = GameJson.Read<FrozenLineup>(participant.LineupSnapshot);
            var skill = lineup.Skills[0] with { ImplementationKey = FactionSkillHandler.Forecast, CooldownTurns = 16 };
            participant.LineupSnapshot = GameJson.Document(lineup with { Skills = [skill, .. lineup.Skills.Skip(1)] });
            var row = await db.MatchStates.SingleAsync(s => s.MatchId == id);
            var state = GameJson.Read<GameState>(row.State);
            state.SkillStates[Side.Red][0] = state.SkillStates[Side.Red][0] with { ImplementationKey = FactionSkillHandler.Forecast };
            row.State = GameJson.Document(state);
            await db.SaveChangesAsync();
        }
        var url = $"/api/v1/matches/{id}";
        var before = (await red.Client.GetFromJsonAsync<MatchStateDto>(url + "/state", Json))!;
        var activate = new MatchCommandRequest(Guid.NewGuid(), before.Version, GameJson.Element(new { type = "team_skill", slot = 1, target = new { } }));
        var pending = await Post<MatchCommandResultDto>(red.Client, url + "/commands", activate);
        Assert.Equal(before.DeadlineAt, pending.Snapshot.DeadlineAt);
        Assert.Equal("red", pending.Snapshot.SideToMove);
        Assert.Equal(before.CountedActions, pending.Snapshot.CountedActions);
        Assert.NotNull(Game(pending.Snapshot).ForecastReview);
        Assert.True((await Post<MatchCommandResultDto>(red.Client, url + "/commands", activate)).Duplicate);
        var reconnected = (await red.Client.GetFromJsonAsync<MatchStateDto>(url + "/state", Json))!;
        Assert.Equal(Game(pending.Snapshot).ForecastReview, Game(reconnected).ForecastReview);
        Assert.Empty((await red.Client.GetFromJsonAsync<LegalMoveDto[]>(url + "/legal-actions", Json))!);
        using var blocked = await red.Client.PostAsJsonAsync(url + "/commands",
            new MatchCommandRequest(Guid.NewGuid(), pending.SequenceNo, GameJson.Element(new { type = "undo" })), Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        var prediction = await red.Client.GetFromJsonAsync<System.Text.Json.JsonElement>(url + "/forecast?version=" + pending.SequenceNo, Json);
        Assert.Equal("black", prediction.GetProperty("prediction").GetProperty("initialBoard").GetProperty("sideToMove").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await black.Client.GetAsync(url + "/forecast?version=" + pending.SequenceNo)).StatusCode);
        if (completion == 2)
        {
            var resigned = await Post<MatchCommandResultDto>(red.Client, url + "/commands",
                new MatchCommandRequest(Guid.NewGuid(), pending.SequenceNo, GameJson.Element(new { type = "resign" })));
            Assert.Equal("completed", resigned.Snapshot.Status);
            Assert.Null(Game(resigned.Snapshot).ForecastReview);
            Assert.Equal(before.CountedActions, resigned.Snapshot.CountedActions);
            return;
        }
        MatchStateDto done;
        if (completion == 1) done = await ExpireTurn(red.Client, id);
        else if (completion == 3)
        {
            await using var timerScope = _factory.Services.CreateAsyncScope();
            var timeout = timerScope.ServiceProvider.GetRequiredService<MatchCommandService>()
                .TimeoutAsync(id, pending.SequenceNo, pending.Snapshot.DeadlineAt!.Value, default);
            var confirm = red.Client.PostAsJsonAsync(url + "/commands",
                new MatchCommandRequest(Guid.NewGuid(), pending.SequenceNo, GameJson.Element(new { type = "end_turn" })), Json);
            await Task.WhenAll(timeout, confirm);
            using var response = await confirm;
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            done = (await red.Client.GetFromJsonAsync<MatchStateDto>(url + "/state", Json))!;
        }
        else
        {
            var end = new MatchCommandRequest(Guid.NewGuid(), pending.SequenceNo, GameJson.Element(new { type = "end_turn" }));
            var confirmed = await Post<MatchCommandResultDto>(red.Client, url + "/commands", end);
            Assert.True((await Post<MatchCommandResultDto>(red.Client, url + "/commands", end)).Duplicate);
            done = confirmed.Snapshot;
        }
        Assert.Equal("black", done.SideToMove);
        Assert.Equal(before.CountedActions + 1, done.CountedActions);
        Assert.Equal(before.TurnIndex + 1, done.TurnIndex);
        Assert.Null(Game(done).ForecastReview);
        Assert.Equal(16, Game(done).SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Equal(0, Game(done).ConsecutiveTimeouts[Side.Red]);
        using var stale = await red.Client.PostAsJsonAsync(url + "/commands",
            new MatchCommandRequest(Guid.NewGuid(), pending.SequenceNo, GameJson.Element(new { type = "end_turn" })), Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var checkScope = _factory.Services.CreateAsyncScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await checkDb.MatchActions.CountAsync(a => a.MatchId == id && a.Kind == "end_turn"));
        await Post<MatchCommandResultDto>(black.Client, url + "/commands",
            new MatchCommandRequest(Guid.NewGuid(), done.Version, GameJson.Element(new { type = "resign" })));
    }
}

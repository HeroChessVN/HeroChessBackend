using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class MatchFlowTests
{
    [Fact]
    public async Task Forecast_survives_later_moves_is_private_and_coalesces_retries_without_mutating_the_match()
    {
        var (id, red, black) = await ForecastFixture();
        var url = $"/api/v1/matches/{id}/forecast?version=1";
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.MatchStates.AsNoTracking().SingleAsync(x => x.MatchId == id);
        var stateJson = before.State.RootElement.GetRawText();
        var actionCount = await db.MatchActions.CountAsync(x => x.MatchId == id);
        long computed = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Name == "herochess.forecast.requests") meter.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "outcome" && Equals(tag.Value, "computed")) Interlocked.Add(ref computed, value);
        });
        listener.Start();
        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => red.Client.GetFromJsonAsync<JsonElement>(url, Json)));
        Assert.Equal(1, computed);
        Assert.All(replies, reply =>
        {
            Assert.Equal(1, reply.GetProperty("version").GetInt32());
            Assert.True(JsonElement.DeepEquals(replies[0], reply));
            Assert.InRange(reply.GetProperty("prediction").GetProperty("variation").GetArrayLength(), 0, 3);
        });
        Assert.Equal(HttpStatusCode.NotFound, (await black.Client.GetAsync(url)).StatusCode);
        var outsider = await CreatePlayerWithLineup();
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync(url)).StatusCode);
        var after = await db.MatchStates.AsNoTracking().SingleAsync(x => x.MatchId == id);
        Assert.Equal(2, after.Version);
        Assert.Equal(stateJson, after.State.RootElement.GetRawText());
        Assert.Equal(actionCount, await db.MatchActions.CountAsync(x => x.MatchId == id));
        var ended = await db.Matches.SingleAsync(x => x.Id == id);
        ended.Status = "completed"; ended.Result = "draw"; ended.EndReason = "action_limit"; ended.EndedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await red.Client.GetAsync(url)).StatusCode);
        foreach (var suffix in new[] { "", "?version=0", "?version=-1", "?version=no", "?version=2147483648" })
            Assert.Equal(HttpStatusCode.BadRequest, (await red.Client.GetAsync($"/api/v1/matches/{id}/forecast{suffix}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await red.Client.GetAsync($"/api/v1/matches/{id}/forecast?version=2")).StatusCode);

        // A populated cache must not replace the activation/owner checks.
        var activation = await db.MatchActions.SingleAsync(x => x.MatchId == id && x.SequenceNo == 1);
        activation.ResolvedEvents = GameJson.Document(new[] { new { type = "team_skill.activated", code = SkillKeys.Khien } });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await red.Client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Forecast_follows_nested_undo_lineage_on_both_cache_hits_and_misses()
    {
        var (id, red, _) = await ForecastFixture();
        var url = $"/api/v1/matches/{id}/forecast?version=";
        Assert.Equal(HttpStatusCode.OK, (await red.Client.GetAsync(url + "1")).StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.MatchStates.SingleAsync(x => x.MatchId == id);
        var action2 = await db.MatchActions.SingleAsync(x => x.MatchId == id && x.SequenceNo == 2);
        action2.Kind = "team_skill"; action2.ActorSide = "red";
        action2.ResolvedEvents = GameJson.Document(new[] { new { type = "team_skill.activated", code = FactionSkillHandler.Forecast } });
        await AppendUndo(3, 0);
        Assert.Equal(HttpStatusCode.NotFound, (await red.Client.GetAsync(url + "1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await red.Client.GetAsync(url + "2")).StatusCode);
        // Undoing the Undo restores the older branch containing both activations.
        await AppendUndo(4, 2);
        Assert.Equal(HttpStatusCode.OK, (await red.Client.GetAsync(url + "1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await red.Client.GetAsync(url + "2")).StatusCode);
        await AppendUndo(5, 1);
        Assert.Equal(HttpStatusCode.OK, (await red.Client.GetAsync(url + "1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await red.Client.GetAsync(url + "2")).StatusCode);

        async Task AppendUndo(int sequence, int target)
        {
            var targetAction = await db.MatchActions.AsNoTracking().SingleAsync(x => x.MatchId == id && x.SequenceNo == target);
            db.MatchActions.Add(ForecastAction(id, sequence, "undo", targetAction.StateAfter,
                new[] { new { type = "match.undone", targetSequence = target } }));
            state.Version = sequence;
            var restored = GameJson.Read<GameState>(targetAction.StateAfter);
            restored.Version = sequence;
            state.State = GameJson.Document(restored);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Forecast_cancellation_during_search_does_not_cache_or_hold_the_gate()
    {
        var (id, red, _) = await ForecastFixture();
        var user = (await red.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        await using var scope = _factory.Services.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var read = new MatchReadService(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            new ForecastCallbackClock(cancellation.Cancel), scope.ServiceProvider.GetRequiredService<PlayerProfileService>(),
            scope.ServiceProvider.GetRequiredService<ILogger<MatchReadService>>());
        var dispatcher = scope.ServiceProvider.GetRequiredService<CommandSkillDispatcher>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.ForecastAsync(user.UserId, id, 1, dispatcher, cancellation.Token));
        Assert.Equal(HttpStatusCode.OK, (await red.Client.GetAsync($"/api/v1/matches/{id}/forecast?version=1")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forecast_allows_a_new_move_but_rejects_an_undo_committed_during_search(bool undo)
    {
        var (id, red, _) = await ForecastFixture();
        var user = (await red.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        await using var scope = _factory.Services.CreateAsyncScope();
        var clock = new ForecastCallbackClock(() =>
        {
            using var concurrentScope = _factory.Services.CreateScope();
            var concurrent = concurrentScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = concurrent.MatchStates.Single(x => x.MatchId == id);
            var original = concurrent.MatchActions.Single(x => x.MatchId == id && x.SequenceNo == 0);
            concurrent.MatchActions.Add(ForecastAction(id, 3, undo ? "undo" : "move", original.StateAfter,
                undo ? new object[] { new { type = "match.undone", targetSequence = 0 } } : Array.Empty<object>()));
            row.Version = 3;
            var updated = GameJson.Read<GameState>(original.StateAfter);
            updated.Version = 3;
            row.State = GameJson.Document(updated);
            concurrent.SaveChanges();
        });
        var read = new MatchReadService(scope.ServiceProvider.GetRequiredService<AppDbContext>(), clock,
            scope.ServiceProvider.GetRequiredService<PlayerProfileService>(), scope.ServiceProvider.GetRequiredService<ILogger<MatchReadService>>());
        var dispatcher = scope.ServiceProvider.GetRequiredService<CommandSkillDispatcher>();
        if (undo)
        {
            var rejected = await Assert.ThrowsAsync<ApiException>(() => read.ForecastAsync(user.UserId, id, 1, dispatcher, default));
            Assert.Equal("FORECAST_NOT_FOUND", rejected.Code);
        }
        else
        {
            var result = GameJson.Element(await read.ForecastAsync(user.UserId, id, 1, dispatcher, default));
            Assert.Equal(1, result.GetProperty("version").GetInt32());
        }
        Assert.True(clock.Called);
    }

    [Fact]
    public async Task Forecast_reports_bad_snapshots_and_bounds_requests_per_player()
    {
        var (id, red, _) = await ForecastFixture();
        var url = $"/api/v1/matches/{id}/forecast?version=1";
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var action = await db.MatchActions.SingleAsync(x => x.MatchId == id && x.SequenceNo == 1);
        action.StateAfter = JsonDocument.Parse("""{"pieces":"invalid"}""");
        await db.SaveChangesAsync();
        using var bad = await red.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Contains("INVALID_FORECAST_SNAPSHOT", await bad.Content.ReadAsStringAsync());
        // Restore the position and prove failures/cancelled work did not cache an invalid result.
        var original = await db.MatchActions.AsNoTracking().SingleAsync(x => x.MatchId == id && x.SequenceNo == 0);
        action.StateAfter = GameJson.Document(GameJson.Read<GameState>(original.StateAfter));
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await red.Client.GetAsync(url)).StatusCode);
        for (var request = 2; request < 30; request++)
            Assert.Equal(HttpStatusCode.OK, (await red.Client.GetAsync(url)).StatusCode);
        using var limited = await red.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("FORECAST_RATE_LIMITED", await limited.Content.ReadAsStringAsync());
    }

    private async Task<(Guid Id, (HttpClient Client, LineupDto Lineup) Red, (HttpClient Client, LineupDto Lineup) Black)> ForecastFixture()
    {
        var match = await Ranked();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.MatchStates.SingleAsync(x => x.MatchId == match.Id);
        var state = GameJson.Read<GameState>(row.State);
        state.Version = 1; state.TurnIndex = 1; state.CountedActions = 1; state.SideToMove = Side.Black;
        var activated = GameJson.Document(state);
        db.MatchActions.Add(ForecastAction(match.Id, 1, "team_skill", activated,
            new[] { new { type = "team_skill.activated", code = FactionSkillHandler.Forecast } }));
        state.Version = 2; state.TurnIndex = 2; state.CountedActions = 2; state.SideToMove = Side.Red;
        row.Version = 2; row.TurnIndex = 2; row.CountedActions = 2; row.SideToMove = "red";
        row.State = GameJson.Document(state); row.TurnDeadlineAt = DateTimeOffset.UtcNow.AddHours(1);
        var response = ForecastAction(match.Id, 2, "move", row.State, Array.Empty<object>());
        response.ActorSide = "black";
        db.MatchActions.Add(response);
        await db.SaveChangesAsync();
        return match;
    }

    private static MatchAction ForecastAction(Guid matchId, int sequence, string kind, JsonDocument state, object events) => new()
    {
        Id = Guid.NewGuid(), MatchId = matchId, CommandId = Guid.NewGuid(), SequenceNo = sequence, Kind = kind, ActorSide = "red",
        StateAfter = JsonDocument.Parse(state.RootElement.GetRawText()), ResolvedEvents = GameJson.Document(events),
        StateSchemaVersion = 4, ReceivedAt = DateTimeOffset.UtcNow, CommittedAt = DateTimeOffset.UtcNow
    };

    private sealed class ForecastCallbackClock(Action callback) : TimeProvider
    {
        public bool Called { get; private set; }
        public override long GetTimestamp()
        {
            if (!Called) { Called = true; callback(); }
            return 0;
        }
    }
}

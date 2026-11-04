using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class MatchFlowTests
{
    // Correctness/measurement harness, not a capacity claim about Azure or the player's client.
    [Fact]
    public async Task Legacy_bot_baseline_records_thirty_turns_without_dropped_work()
    {
        var timings = new ConcurrentDictionary<string, ConcurrentBag<double>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == BotTelemetry.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            timings.GetOrAdd(instrument.Name, _ => new()).Add(value));
        listener.Start();
        var human = await CreatePlayerWithLineup();
        var ticket = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        var matchId = ticket.MatchId!.Value;
        await Select(human, matchId);
        (await human.Client.PostAsync($"/api/v1/matches/{matchId}/confirm", null)).EnsureSuccessStatusCode();
        for (var turn = 0; turn < 30; turn++)
        {
            var state = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json))!;
            var moves = (await human.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{matchId}/legal-actions", Json))!;
            Assert.NotEmpty(moves);
            var moved = await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{matchId}/commands", Command(state.Version, moves[0]));
            var wait = Stopwatch.StartNew();
            MatchStateDto after;
            do
            {
                await Task.Delay(20);
                after = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json))!;
            } while (after.Version <= moved.SequenceNo && wait.Elapsed < TimeSpan.FromSeconds(10));
            Assert.Equal(moved.SequenceNo + 1, after.Version);
            // Reset the opening so all 30 samples stay non-terminal. This is an assisted test match.
            await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{matchId}/commands",
                new MatchCommandRequest(Guid.NewGuid(), after.Version, JsonSerializer.SerializeToElement(new { type = "undo", targetSequence = 0 }, Json)));
        }
        Assert.True(timings.TryGetValue("herochess.bot.total", out var totals));
        Assert.Equal(30, totals!.Count);
        foreach (var (name, samples) in timings.OrderBy(x => x.Key))
        {
            var sorted = samples.Order().ToArray();
            Console.WriteLine($"BASELINE {name} n={sorted.Length} p50_ms={sorted[(int)Math.Ceiling(sorted.Length * .50) - 1] * 1000:F2} p95_ms={sorted[(int)Math.Ceiling(sorted.Length * .95) - 1] * 1000:F2}");
        }
        var final = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{matchId}/state", Json))!;
        await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{matchId}/commands", new MatchCommandRequest(Guid.NewGuid(), final.Version,
            JsonSerializer.SerializeToElement(new { type = "resign" }, Json)));
    }
}

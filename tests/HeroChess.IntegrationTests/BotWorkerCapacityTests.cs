using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using static HeroChess.IntegrationTests.MatchFlowTests;

namespace HeroChess.IntegrationTests;

public sealed class BotWorkerCapacityTests(HeroChessFactory factory) : IClassFixture<HeroChessFactory>
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Fixed_worker_count_bounds_inflight_and_completes_independent_matches(int workers)
    {
        await factory.Services.GetRequiredService<BotTurnScheduler>().StopAsync(default);
        var matches = new List<(HttpClient Client, Guid Id)>();
        using var scheduler = new BotTurnScheduler(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<ILogger<BotTurnScheduler>>(), Options.Create(new MatchRuntimeOptions { BotWorkerCount = workers }));
        long running = 0, peak = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) => { if (instrument.Name == "herochess.bot.in_flight") meter.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            var total = Interlocked.Add(ref running, value);
            long before;
            do { before = Volatile.Read(ref peak); if (before >= total) break; }
            while (Interlocked.CompareExchange(ref peak, total, before) != before);
        });
        listener.Start();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var player = await new MatchFlowTests(factory).CreatePlayerWithLineup();
                var ticket = await Post<MatchmakingTicketDto>(player.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot", "hard"));
                var id = ticket.MatchId!.Value; matches.Add((player.Client, id));
                await Select(player, id);
                (await player.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
                var moves = (await player.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{id}/legal-actions", Json))!;
                await Post<MatchCommandResultDto>(player.Client, $"/api/v1/matches/{id}/commands", Command(0, moves[0]));
                scheduler.Schedule(id, 1);
            }
            await scheduler.StartAsync(default);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            foreach (var (client, id) in matches)
            {
                MatchStateDto state;
                do
                {
                    await Task.Delay(25, deadline.Token);
                    state = (await client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json, deadline.Token))!;
                } while (state.Version == 1);
                Assert.Equal(2, state.Version); Assert.Equal("red", state.SideToMove);
            }
            await scheduler.StopAsync(default);
            Assert.Equal(workers, Volatile.Read(ref peak));
            Assert.Equal(0, Volatile.Read(ref running));
        }
        finally
        {
            await scheduler.StopAsync(default);
            foreach (var (client, id) in matches)
            {
                var state = (await client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
                if (state.Status == "active") await Post<MatchCommandResultDto>(client, $"/api/v1/matches/{id}/commands",
                    new MatchCommandRequest(Guid.NewGuid(), state.Version, System.Text.Json.JsonSerializer.SerializeToElement(new { type = "resign" }, Json)));
            }
        }
    }
}

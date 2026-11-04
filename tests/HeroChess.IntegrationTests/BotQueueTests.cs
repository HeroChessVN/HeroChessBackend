using HeroChess.Api.Services;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class BotQueueTests
{
    [Fact]
    public async Task Queue_metrics_measure_wait_without_match_or_player_labels()
    {
        var values = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "herochess.bot.queue_wait") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            Assert.Equal("s", instrument.Unit);
            Assert.Empty(tags.ToArray());
            values.Add(value);
        });
        listener.Start();
        var queue = new BotWorkQueue(1);
        queue.Schedule(Guid.NewGuid(), 4);
        var work = await queue.TakeAsync(CancellationToken.None);
        queue.Complete(work);
        Assert.Single(values);
        Assert.True(values[0] >= 0);
    }

    [Fact]
    public void Disabled_export_needs_no_credentials_but_enabled_export_requires_https()
    {
        var services = new ServiceCollection();
        services.AddBotObservability(new ConfigurationBuilder().Build());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Observability:Enabled"] = "true",
            ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = "http://insecure.example/otlp/v1/metrics"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => services.AddBotObservability(config));
    }

    [Fact]
    public async Task Full_queue_does_not_poison_retry_and_inflight_work_is_deduplicated()
    {
        var queue = new BotWorkQueue(1);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.True(queue.Schedule(first, 1));
        Assert.False(queue.Schedule(second, 1));
        Assert.Equal(1, queue.Waiting);
        var item = await queue.TakeAsync(CancellationToken.None);
        Assert.Equal(0, queue.Waiting);
        Assert.Equal(1, queue.InFlight);
        Assert.False(queue.Schedule(first, 1));
        Assert.True(queue.Schedule(second, 1));
        queue.Complete(item);
        queue.Complete(item); // cleanup must not decrement twice
        Assert.Equal(0, queue.InFlight);
        var next = await queue.TakeAsync(CancellationToken.None);
        Assert.Equal(second, next.MatchId);
        queue.Complete(next);
        Assert.True(queue.Schedule(first, 1));
        queue.Stop();
    }

    [Fact]
    public async Task Cancelled_reader_does_not_consume_capacity_or_work()
    {
        var queue = new BotWorkQueue(1);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queue.TakeAsync(cancelled.Token));
        Assert.Equal(0, queue.InFlight);
        Assert.True(queue.Schedule(Guid.NewGuid(), 1));
        queue.Stop();
    }
}

using System.Diagnostics.Metrics;

namespace HeroChess.Api.Services;

// Bounded labels only. Match/player identifiers belong in diagnostic logs, never metric tags.
public static class BotTelemetry
{
    public const string MeterName = "HeroChess.Bot";
    public static readonly Meter Meter = new(MeterName, "1.0.0");
    public static readonly Counter<long> Scheduled = Meter.CreateCounter<long>("herochess.bot.schedule");
    public static readonly Counter<long> Turns = Meter.CreateCounter<long>("herochess.bot.turns");
    public static readonly UpDownCounter<long> Waiting = Meter.CreateUpDownCounter<long>("herochess.bot.queue.waiting");
    public static readonly UpDownCounter<long> InFlight = Meter.CreateUpDownCounter<long>("herochess.bot.in_flight");
    public static readonly Histogram<double> QueueWait = Meter.CreateHistogram<double>("herochess.bot.queue_wait", "s");
    public static readonly Histogram<double> StateRead = Meter.CreateHistogram<double>("herochess.bot.state_read", "s");
    public static readonly Histogram<double> Search = Meter.CreateHistogram<double>("herochess.bot.search", "s");
    public static readonly Histogram<double> Command = Meter.CreateHistogram<double>("herochess.bot.command", "s");
    public static readonly Histogram<double> Total = Meter.CreateHistogram<double>("herochess.bot.total", "s");
    public static readonly Histogram<double> DbCommit = Meter.CreateHistogram<double>("herochess.match.db_commit", "s");
}

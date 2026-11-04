using System.Diagnostics;
using System.Threading.Channels;

namespace HeroChess.Api.Services;

public sealed record BotWorkItem(Guid MatchId, int Version, long EnqueuedAt);

// Pending includes in-flight jobs. A failed TryWrite must release its dedup key for maintenance retry.
public sealed class BotWorkQueue
{
    private readonly Channel<BotWorkItem> _channel;
    private readonly object _gate = new();
    private readonly HashSet<(Guid, int)> _pending = new();
    private readonly HashSet<(Guid, int)> _inFlight = new();
    private int _waiting;
    public int Waiting { get { lock (_gate) return _waiting; } }
    public int InFlight { get { lock (_gate) return _inFlight.Count; } }

    public BotWorkQueue(int capacity)
    {
        if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        _channel = Channel.CreateBounded<BotWorkItem>(new BoundedChannelOptions(capacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public bool Schedule(Guid matchId, int version)
    {
        lock (_gate)
        {
            var key = (matchId, version);
            if (!_pending.Add(key)) { Count("deduplicated"); return false; }
            if (!_channel.Writer.TryWrite(new(matchId, version, Stopwatch.GetTimestamp())))
            { _pending.Remove(key); Count("full"); return false; }
            _waiting++;
            BotTelemetry.Waiting.Add(1);
            Count("accepted");
            return true;
        }
    }

    public async ValueTask<BotWorkItem> TakeAsync(CancellationToken ct)
    {
        var item = await _channel.Reader.ReadAsync(ct);
        lock (_gate)
        {
            _waiting--;
            _inFlight.Add((item.MatchId, item.Version));
            BotTelemetry.Waiting.Add(-1);
            BotTelemetry.InFlight.Add(1);
        }
        BotTelemetry.QueueWait.Record(Stopwatch.GetElapsedTime(item.EnqueuedAt).TotalSeconds);
        return item;
    }

    public void Complete(BotWorkItem item)
    {
        lock (_gate)
        {
            if (!_inFlight.Remove((item.MatchId, item.Version))) return;
            _pending.Remove((item.MatchId, item.Version));
            BotTelemetry.InFlight.Add(-1);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _channel.Writer.TryComplete();
            while (_channel.Reader.TryRead(out var item)) _pending.Remove((item.MatchId, item.Version));
            BotTelemetry.Waiting.Add(-_waiting);
            _waiting = 0;
        }
    }

    private static void Count(string result) => BotTelemetry.Scheduled.Add(1, new KeyValuePair<string, object?>("result", result));
}

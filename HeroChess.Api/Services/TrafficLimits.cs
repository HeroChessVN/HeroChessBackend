using System.Threading.RateLimiting;
using HeroChess.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Services;

public sealed class TrafficLimitsOptions
{
    public int MaxSocketsPerAccount { get; set; } = 2;
    public int MaxSockets { get; set; } = 256;
    public int MaxHttpInFlight { get; set; } = 32;
    public int MaxPendingTickets { get; set; } = 512;
    public TokenBucketRateLimiterOptions Global { get; set; } = Bucket(400, 200);
    public TokenBucketRateLimiterOptions Ip { get; set; } = Bucket(300, 150);
    public TokenBucketRateLimiterOptions Authentication { get; set; } = Bucket(12, 2);
    public TokenBucketRateLimiterOptions Account { get; set; } = Bucket(40, 20);
    public TokenBucketRateLimiterOptions Reads { get; set; } = Bucket(12, 4);
    public TokenBucketRateLimiterOptions Tickets { get; set; } = Bucket(4, 1, 5);
    public TokenBucketRateLimiterOptions Messages { get; set; } = Bucket(20, 10);

    public IEnumerable<TokenBucketRateLimiterOptions> Buckets => [Global, Ip, Authentication, Account, Reads, Tickets, Messages];
    private static TokenBucketRateLimiterOptions Bucket(int burst, int tokens, int seconds = 1) => new()
    {
        TokenLimit = burst, TokensPerPeriod = tokens, ReplenishmentPeriod = TimeSpan.FromSeconds(seconds),
        QueueLimit = 0, AutoReplenishment = true
    };

    public bool IsValid() => MaxSocketsPerAccount is >= 1 and <= 8 && MaxSockets >= MaxSocketsPerAccount && MaxSockets <= 4096 &&
        MaxHttpInFlight is >= 1 and <= 256 && MaxPendingTickets is >= 1 and <= 4096 &&
        Buckets.All(b => b is not null && b.TokenLimit > 0 && b.TokensPerPeriod > 0 && b.TokensPerPeriod <= b.TokenLimit &&
            b.ReplenishmentPeriod >= TimeSpan.FromMilliseconds(100) && b.ReplenishmentPeriod <= TimeSpan.FromHours(1) &&
            b.QueueLimit == 0 && b.AutoReplenishment);
}

// Process-local limits: REST and every socket of an account share the same budgets.
public sealed class TrafficLimits : IDisposable
{
    private readonly TokenBucketRateLimiter _global;
    private readonly PartitionedRateLimiter<(string Address, bool Authentication)> _ips;
    private readonly PartitionedRateLimiter<(Guid Account, string Lane)> _accounts;
    private readonly ConcurrencyLimiter _http;
    private readonly ConcurrencyLimiter _sockets;
    private readonly PartitionedRateLimiter<Guid> _accountSockets;

    public TrafficLimits(IOptions<TrafficLimitsOptions> options)
    {
        var config = options.Value;
        _global = new(config.Global);
        _ips = PartitionedRateLimiter.Create<(string Address, bool Authentication), (string, bool)>(key =>
            RateLimitPartition.GetTokenBucketLimiter(key, _ => key.Authentication ? config.Authentication : config.Ip));
        _accounts = PartitionedRateLimiter.Create<(Guid Account, string Lane), (Guid, string)>(key =>
            RateLimitPartition.GetTokenBucketLimiter(key, _ => key.Lane switch
            {
                "reads" => config.Reads, "tickets" => config.Tickets, "messages" => config.Messages, _ => config.Account
            }));
        _http = new(new() { PermitLimit = config.MaxHttpInFlight, QueueLimit = 0 });
        _sockets = new(new() { PermitLimit = config.MaxSockets, QueueLimit = 0 });
        _accountSockets = PartitionedRateLimiter.Create<Guid, Guid>(account => RateLimitPartition.GetConcurrencyLimiter(account,
            _ => new() { PermitLimit = config.MaxSocketsPerAccount, QueueLimit = 0 }));
    }

    public void CheckAccount(Guid account, string lane = "account")
    {
        using var lease = _accounts.AttemptAcquire((account, lane));
        Require(lease, "RATE_LIMITED");
    }

    public void CheckAddress(string address, bool authentication = false)
    {
        if (authentication)
        {
            using var auth = _ips.AttemptAcquire((address, true));
            Require(auth, "RATE_LIMITED");
        }
        using var ip = _ips.AttemptAcquire((address, false));
        Require(ip, "RATE_LIMITED");
        CheckGlobal();
    }

    public void CheckMessage(Guid account)
    {
        CheckAccount(account);
        CheckAccount(account, "messages");
        CheckGlobal();
    }

    private void CheckGlobal()
    {
        using var global = _global.AttemptAcquire();
        Require(global, "SERVER_BUSY");
    }

    public RateLimitLease EnterHttp()
    {
        var lease = _http.AttemptAcquire();
        if (lease.IsAcquired) return lease;
        lease.Dispose();
        throw Rejected("SERVER_BUSY", 1);
    }

    public IDisposable OpenSocket(Guid account)
    {
        var own = _accountSockets.AttemptAcquire(account);
        if (!own.IsAcquired) { own.Dispose(); throw Rejected("SOCKET_LIMIT", 5); }
        var global = _sockets.AttemptAcquire();
        if (global.IsAcquired) return new SocketLease(own, global);
        own.Dispose(); global.Dispose();
        throw Rejected("SOCKET_LIMIT", 5);
    }

    private sealed class SocketLease(RateLimitLease account, RateLimitLease global) : IDisposable
    {
        public void Dispose() { account.Dispose(); global.Dispose(); }
    }

    private static void Require(RateLimitLease lease, string code)
    {
        if (!lease.IsAcquired)
            throw Rejected(code, lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)) : 1);
    }

    public static ApiException Rejected(string code, int seconds) => new(429, code,
        "Too many requests or connections. Retry after the indicated delay.", new { retryAfterSeconds = seconds }, seconds);

    public void Dispose()
    {
        _global.Dispose(); _ips.Dispose(); _accounts.Dispose(); _http.Dispose(); _sockets.Dispose(); _accountSockets.Dispose();
    }
}

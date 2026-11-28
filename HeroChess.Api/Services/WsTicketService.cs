// Vai trò file: Vé WebSocket ngẫu nhiên, ngắn hạn, dùng một lần; tránh đưa bearer token dài hạn lên URL.
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Services;

public sealed class WsTicketService(IOptions<MatchRuntimeOptions> options, TimeProvider clock, IOptions<TrafficLimitsOptions> traffic)
{
    // Entry: Player và hạn dùng của một vé socket trong bộ nhớ.
    private sealed record Entry(Guid PlayerId, DateTimeOffset ExpiresAt);
    private readonly Dictionary<string, Entry> _tickets = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    // Issue: Sinh 32 byte ngẫu nhiên, lưu player và hạn dùng; dọn vé đã hết hạn.
    public (string Ticket, DateTimeOffset ExpiresAt) Issue(Guid playerId)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            foreach (var stale in _tickets.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray()) _tickets.Remove(stale);
            if (_tickets.Count >= traffic.Value.MaxPendingTickets || _tickets.Values.Count(x => x.PlayerId == playerId) >= 4)
                throw TrafficLimits.Rejected("TICKET_LIMIT", 5);
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var expires = now.AddSeconds(options.Value.WebSocketTicketSeconds);
            _tickets[token] = new(playerId, expires);
            return (token, expires);
        }
    }

    // TryConsume: Lấy và xóa vé nguyên tử; vé đã dùng/hết hạn không được chấp nhận.
    public bool TryConsume(string token, out Guid playerId)
    {
        lock (_gate)
        {
            playerId = default;
            if (!_tickets.Remove(token, out var entry) || entry.ExpiresAt <= clock.GetUtcNow()) return false;
            playerId = entry.PlayerId; return true;
        }
    }
}

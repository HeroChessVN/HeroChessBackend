// Vai trò file: Vé WebSocket ngẫu nhiên, ngắn hạn, dùng một lần; tránh đưa bearer token dài hạn lên URL.
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Services;

public sealed class WsTicketService(IOptions<MatchRuntimeOptions> options, TimeProvider clock)
{
    // Entry: Player và hạn dùng của một vé socket trong bộ nhớ.
    private sealed record Entry(Guid PlayerId, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Entry> _tickets = new(StringComparer.Ordinal);

    // Issue: Sinh 32 byte ngẫu nhiên, lưu player và hạn dùng; dọn vé đã hết hạn.
    public (string Ticket, DateTimeOffset ExpiresAt) Issue(Guid playerId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = clock.GetUtcNow().AddSeconds(options.Value.WebSocketTicketSeconds);
        _tickets[token] = new(playerId, expires);
        foreach (var stale in _tickets.Where(x => x.Value.ExpiresAt <= clock.GetUtcNow()).Select(x => x.Key)) _tickets.TryRemove(stale, out _);
        return (token, expires);
    }

    // TryConsume: Lấy và xóa vé nguyên tử; vé đã dùng/hết hạn không được chấp nhận.
    public bool TryConsume(string token, out Guid playerId)
    {
        playerId = default;
        if (!_tickets.TryRemove(token, out var entry) || entry.ExpiresAt <= clock.GetUtcNow()) return false;
        playerId = entry.PlayerId; return true;
    }
}

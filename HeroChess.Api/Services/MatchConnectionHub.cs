// Vai trò file: Registry socket + outbox riêng mỗi connection; giữ thứ tự gửi mà không chờ mạng trong khóa trận.
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using HeroChess.Api.Infrastructure;

namespace HeroChess.Api.Services;

public sealed class MatchConnectionHub
{
    // Connection: State riêng một socket: subscription, outbox giới hạn và tín hiệu hủy.
    private sealed class Connection(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;
        public ConcurrentDictionary<Guid, byte> Matches { get; } = new();
        public Channel<byte[]> Outbox { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        public CancellationTokenSource Lifetime { get; } = new();
    }
    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();
    // Add: Đăng ký socket và khởi chạy send pump riêng.
    public Guid Add(WebSocket socket)
    {
        var id = Guid.NewGuid(); var connection = new Connection(socket);
        _connections[id] = connection;
        _ = PumpAsync(id, connection);
        return id;
    }
    // Subscribe: Đánh dấu connection muốn nhận broadcast của match; caller phải authorize trước.
    public void Subscribe(Guid connectionId, Guid matchId)
    { if (_connections.TryGetValue(connectionId, out var item)) item.Matches[matchId] = 0; }
    // Remove: Gỡ connection, đóng queue, hủy lifetime và abort socket.
    public void Remove(Guid connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var item)) return;
        item.Outbox.Writer.TryComplete();
        item.Lifetime.Cancel();
        item.Socket.Abort();
    }
    // Callers may hold a match lock: only enqueue here, never await network I/O.
    // BroadcastAsync: Serialize một lần rồi enqueue cho các subscriber; hoàn thành nghĩa là đã enqueue, chưa chắc client nhận.
    public Task BroadcastAsync(Guid matchId, object value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, GameJson.Options);
        foreach (var pair in _connections.Where(x => x.Value.Matches.ContainsKey(matchId))) Enqueue(pair.Key, pair.Value, bytes);
        return Task.CompletedTask;
    }
    // SendAsync: Enqueue message riêng cho một connection, ví dụ snapshot hoặc ACK retry.
    public Task SendAsync(Guid connectionId, object value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_connections.TryGetValue(connectionId, out var item))
            Enqueue(connectionId, item, JsonSerializer.SerializeToUtf8Bytes(value, GameJson.Options));
        return Task.CompletedTask;
    }
    // Enqueue: Ghi vào outbox giới hạn 64 message; đầy queue thì ngắt client chậm để reconnect.
    private void Enqueue(Guid id, Connection item, byte[] bytes)
    {
        // Slow recipients reconnect/resync instead of retaining unbounded snapshots.
        if (!item.Outbox.Writer.TryWrite(bytes)) Remove(id);
    }
    // PumpAsync: Đọc FIFO và gửi từng message với timeout 5 giây; lỗi mạng/ngắt thì cleanup.
    private async Task PumpAsync(Guid id, Connection item)
    {
        try
        {
            await foreach (var bytes in item.Outbox.Reader.ReadAllAsync(item.Lifetime.Token))
            {
                if (item.Socket.State != WebSocketState.Open) break;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(item.Lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await item.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token).WaitAsync(timeout.Token);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
        finally { Remove(id); }
    }
}

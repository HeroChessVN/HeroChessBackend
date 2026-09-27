// Vai trò file: Test hub với socket giả bị nghẽn để kiểm tra hàng đợi, cách ly người nhận và giới hạn buffer.
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using HeroChess.Api.Services;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class ConnectionHubTests
{
    [Fact]
    // Slow_socket_does_not_block_other_recipients_and_overflow_disconnects_it: Một socket chậm không cản socket khác; đầy queue thì ngắt socket chậm.
    public async Task Slow_socket_does_not_block_other_recipients_and_overflow_disconnects_it()
    {
        var hub = new MatchConnectionHub();
        using var slow = new ProbeSocket(true); using var fast = new ProbeSocket(false);
        var slowId = hub.Add(slow); var fastId = hub.Add(fast);
        var common = Guid.NewGuid(); var slowOnly = Guid.NewGuid();
        hub.Subscribe(slowId, common); hub.Subscribe(fastId, common); hub.Subscribe(slowId, slowOnly);
        try
        {
            await hub.BroadcastAsync(common, new { type = "test", sequence = 1 }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
            await slow.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var delivered = await fast.Delivered.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Contains("\"sequence\":1", delivered);
            for (var i = 0; i < 70; i++) await hub.BroadcastAsync(slowOnly, new { sequence = i }, CancellationToken.None);
            await slow.Aborted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await hub.BroadcastAsync(common, new { sequence = 2 }, CancellationToken.None);
            Assert.Contains("\"sequence\":2", await fast.Delivered.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally { hub.Remove(slowId); hub.Remove(fastId); }
    }

    private sealed class ProbeSocket(bool stall) : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        public TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Aborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Channel<string> Delivered { get; } = Channel.CreateUnbounded<string>();
        public override WebSocketState State => _state;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        // Abort: Đánh dấu socket giả đã hủy và báo signal cho test.
        public override void Abort() { _state = WebSocketState.Aborted; Aborted.TrySetResult(); }
        // Dispose: Dọn socket giả bằng Abort.
        public override void Dispose() => Abort();
        // CloseAsync: Mô phỏng đóng socket ngay.
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { Abort(); return Task.CompletedTask; }
        // CloseOutputAsync: Ủy quyền việc đóng cho CloseAsync.
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => CloseAsync(closeStatus, statusDescription, cancellationToken);
        // ReceiveAsync: Socket giả chỉ phục vụ test gửi; gọi nhận sẽ báo không hỗ trợ.
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => throw new NotSupportedException();
        // SendAsync: Mô phỏng gửi thành công hoặc treo đến khi bị hủy; ghi message vào channel để assert.
        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            SendStarted.TrySetResult();
            if (stall) await Task.Delay(Timeout.Infinite, cancellationToken);
            else Delivered.Writer.TryWrite(Encoding.UTF8.GetString(buffer));
        }
    }
}

// Vai trò file: FIFO gate theo match trong một API process; gần mutex bất đồng bộ, không phải distributed lock.
using System.Collections.Concurrent;

namespace HeroChess.Api.Services;

// MatchLockRegistry: Registry singleton lấy gate theo matchId.
public sealed class MatchLockRegistry
{
    private readonly ConcurrentDictionary<Guid, MatchGate> _locks = new();
    // For: Lấy hoặc tạo gate dùng chung của một matchId.
    public MatchGate For(Guid matchId) => _locks.GetOrAdd(matchId, static _ => new MatchGate());
}

// MatchGate: Hàng đợi chờ FIFO trong process; caller phải Release trong finally.
public sealed class MatchGate
{
    private readonly object _sync = new();
    private readonly Queue<TaskCompletionSource> _waiters = new();
    private bool _held;

    // WaitAsync: Giành gate ngay nếu rảnh; nếu bận thì xếp waiter FIFO và hỗ trợ hủy lúc chờ.
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_held) { _held = true; return Task.CompletedTask; }
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            if (cancellationToken.CanBeCanceled) cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
            return waiter.Task;
        }
    }

    // Release: Đánh thức waiter hợp lệ tiếp theo; bỏ qua waiter bị hủy, hoặc trả gate về trạng thái rảnh.
    public void Release()
    {
        lock (_sync)
        {
            while (_waiters.Count > 0)
                if (_waiters.Dequeue().TrySetResult()) return;
            _held = false;
        }
    }
}

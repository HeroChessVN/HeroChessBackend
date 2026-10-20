// Vai trò file: FIFO gate theo match trong một API process; gần mutex bất đồng bộ, không phải distributed lock.
namespace HeroChess.Api.Services;

// MatchLockRegistry: Registry singleton lấy gate theo matchId.
public sealed class MatchLockRegistry
{
    // ponytail: 4096 stripes bound memory; colliding matches serialize. Increase stripes if gate-wait metrics show contention.
    private readonly MatchGate[] _locks = Enumerable.Range(0, 4096).Select(_ => new MatchGate()).ToArray();
    // For: Lấy gate ổn định cho cùng matchId.
    public MatchGate For(Guid matchId) => _locks[(int)(unchecked((uint)matchId.GetHashCode()) % (uint)_locks.Length)];
}

// MatchGate: Hàng đợi chờ FIFO trong process; caller phải Release trong finally.
public sealed class MatchGate
{
    private readonly object _sync = new();
    private readonly Queue<Waiter> _waiters = new();
    private bool _held;

    private sealed class Waiter
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Registration { get; set; }
    }

    // WaitAsync: Giành gate ngay nếu rảnh; nếu bận thì xếp waiter FIFO và hỗ trợ hủy lúc chờ.
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_held) { _held = true; return Task.CompletedTask; }
            var waiter = new Waiter();
            _waiters.Enqueue(waiter);
            if (cancellationToken.CanBeCanceled) waiter.Registration = cancellationToken.Register(() => waiter.Completion.TrySetCanceled(cancellationToken));
            return waiter.Completion.Task;
        }
    }

    // Release: Đánh thức waiter hợp lệ tiếp theo; bỏ qua waiter bị hủy, hoặc trả gate về trạng thái rảnh.
    public void Release()
    {
        lock (_sync)
        {
            while (_waiters.Count > 0)
            {
                var waiter = _waiters.Dequeue();
                waiter.Registration.Dispose();
                if (waiter.Completion.TrySetResult()) return;
            }
            _held = false;
        }
    }
}

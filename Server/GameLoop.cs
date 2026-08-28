using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// 单线程主循环（R2 强制条款落地）：
    /// 唯一消费者独占世界状态；IO 线程只 Enqueue，不触碰状态。
    /// 心跳间隔由 TickInterval 节流；队列非空时每 tick 清空一次。
    /// </summary>
    public sealed class GameLoop<T> : IDisposable
    {
        private readonly TimeSpan _tickInterval;
        private readonly Action<T> _handler;
        private readonly Action<long>? _onTick;
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _loopTask;
        private long _tickCount;
        private bool _disposed;

        /// <summary>已执行 tick 数（供 WorldSnapshot.Tick 复用）</summary>
        public long TickCount => Interlocked.Read(ref _tickCount);

        public GameLoop(TimeSpan tickInterval, Action<T> handler, ILogger logger, Action<long>? onTick = null)
        {
            _tickInterval = tickInterval;
            _handler = handler;
            _logger = logger;
            _onTick = onTick;
        }

        public void Start()
        {
            if (_loopTask != null) throw new InvalidOperationException("GameLoop already started");
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void Enqueue(T packet)
        {
            if (_disposed) return;
            _queue.Enqueue(packet);
        }

        /// <summary>
        /// 入队一个动作（在主循环线程执行）——供非主循环线程的清理/断开路径
        /// 触达世界状态（R2：世界状态只允许主循环线程直写）
        /// </summary>
        public void EnqueueAction(Action action)
        {
            if (_disposed) return;
            _actionQueue.Enqueue(action);
        }

        public async Task Stop()
        {
            _cts.Cancel();
            if (_loopTask != null)
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;

                // 先 drain 动作队列（断开/清理等世界状态变更），再 drain 数据队列
                while (_actionQueue.TryDequeue(out var action))
                {
                    try { action(); }
                    catch (Exception ex) { _logger.LogError(ex, "GameLoop action error"); }
                }

                // 清空当前数据队列（单线程独占语义：处理期间新入队留待下一 tick）
                while (_queue.TryDequeue(out var packet))
                {
                    try { _handler(packet); }
                    catch (Exception ex) { _logger.LogError(ex, "GameLoop handler error"); }
                }

                long tick = Interlocked.Increment(ref _tickCount);
                _onTick?.Invoke(tick);

                var elapsed = DateTime.UtcNow - started;
                var remaining = _tickInterval - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(remaining, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // 正常停止：取消信号到达延迟等待，干净退出（与 KcpServerSession.UpdateLoopAsync 同模式）
                        break;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}

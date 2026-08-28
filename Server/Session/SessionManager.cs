using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// 会话管理器 —— 收敛 Server3C 散落的会话查找/创建/绑定
    /// （P0 终审 MUST-DO-3：消除 O(n) 端点扫描与 check-then-create 竞态；
    /// conv 采纳策略：服务器采纳客户端首包 conv，见 KcpServerSession）
    /// 线程安全：IO 线程唯一访问点（GetOrCreateSession），会话内发送由 _sendLock 保证
    /// </summary>
    public sealed class SessionManager : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, KcpServerSession> _sessions = new ConcurrentDictionary<string, KcpServerSession>();
        private long _nextSessionId = 1;
        private bool _disposed;

        public int SessionCount => _sessions.Count;
        public IEnumerable<KcpServerSession> AllSessions => _sessions.Values;

        /// <summary>新会话建立（仅胜者触发一次；Server3C 借此做事件接线，避免重复订阅）</summary>
        public event Action<KcpServerSession>? SessionCreated;

        public SessionManager(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 查找或原子创建会话（并发首包同端点 → 恰一个会话）
        /// </summary>
        public KcpServerSession GetOrCreateSession(EndPoint remoteEndPoint, byte[] firstPacket)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SessionManager));

            string key = remoteEndPoint.ToString()!;
            if (_sessions.TryGetValue(key, out var existing))
                return existing;

            long sessionId = Interlocked.Increment(ref _nextSessionId);
            uint? kcpConv = firstPacket != null && firstPacket.Length >= 24
                ? BinaryPrimitives.ReadUInt32LittleEndian(firstPacket)
                : null;

            var created = new KcpServerSession(sessionId, remoteEndPoint, kcpConv, _logger);
            // GetOrAdd：并发下只有一个 created 成为胜者，败者的会话被立即释放
            var winner = _sessions.GetOrAdd(key, created);
            if (!ReferenceEquals(winner, created))
            {
                created.Dispose();
                return winner;
            }

            _logger.LogInformation($"New KCP session: {sessionId} from {remoteEndPoint} (conv={kcpConv})");
            SessionCreated?.Invoke(winner);
            return winner;
        }

        public KcpServerSession? FindByEndpoint(EndPoint remoteEndPoint)
        {
            return _sessions.TryGetValue(remoteEndPoint.ToString()!, out var s) ? s : null;
        }

        public void RemoveByEndpoint(EndPoint remoteEndPoint)
        {
            _sessions.TryRemove(remoteEndPoint.ToString()!, out _);
        }

        public async Task ShutdownAll(string reason)
        {
            foreach (var s in _sessions.Values)
            {
                await s.CloseAsync(reason);
            }
            _sessions.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var s in _sessions.Values)
                s.Dispose();
            _sessions.Clear();
        }
    }
}

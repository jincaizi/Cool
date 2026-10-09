using System;
using System.Threading.Tasks;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// RegisterSession 幂等接线测试（审查回归：新公开入口把 EnsureWired 的幂等语义丢了）。
/// 不幂等的后果是静默的：DataReceived 挂两遍 → 每个包被 ProcessPacket 处理两次
/// （登录建两个 PlayerState、施法裁决两次）。
/// 断言口径：多播委托的每个订阅恰被调用一次，故"订阅数 = 事件触发时处理器被调用的次数"。
/// Server3C 仅构造不 StartAsync（同 VersionRejectTests——不开 socket，EnqueueAction 只入队）。
/// </summary>
public class RegisterSessionTests : IDisposable
{
    private readonly Server3C _server = new Server3C(new TestLogger());

    public void Dispose() => _server.Dispose();

    [Fact]
    public void RegisterSession_Twice_AttachesHandlersOnce()
    {
        var session = new FakeSession(sessionId: 1);

        _server.RegisterSession(session);
        _server.RegisterSession(session);
        _server.RegisterSession(session);

        Assert.Equal(1, session.DataReceivedSubscribers);
        Assert.Equal(1, session.DisconnectedSubscribers);
        Assert.Equal(1, session.ErrorSubscribers);
    }

    [Fact]
    public void RegisterSession_DistinctSessions_EachWiredOnce()
    {
        var a = new FakeSession(sessionId: 1);
        var b = new FakeSession(sessionId: 2);

        _server.RegisterSession(a);
        _server.RegisterSession(b);
        _server.RegisterSession(a);

        Assert.Equal(1, a.DataReceivedSubscribers);
        Assert.Equal(1, b.DataReceivedSubscribers);
    }

    [Fact]
    public void RegisterSession_AfterDisconnect_IsClean()
    {
        var session = new FakeSession(sessionId: 1);
        _server.RegisterSession(session);

        session.RaiseDisconnected();

        // 断开即退订：簿记释放，且无残留订阅（会话断开后不该再往已死的会话上派发）
        Assert.Equal(0, session.DataReceivedSubscribers);
        Assert.Equal(0, session.DisconnectedSubscribers);
        Assert.Equal(0, session.ErrorSubscribers);

        // 重新注册仍然干净——簿记确实被释放了（若未释放，这一步订阅数会停在 0）
        _server.RegisterSession(session);
        Assert.Equal(1, session.DataReceivedSubscribers);
    }

    /// <summary>计数版假会话：把三个事件的订阅数暴露成断言点（无需真 socket）</summary>
    private sealed class FakeSession : IServerSession
    {
        public FakeSession(long sessionId) => SessionId = sessionId;

        public long SessionId { get; }
        public bool IsConnected => true;
        public DateTime LastActivityTime => DateTime.UtcNow;

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? Error;

        public int DataReceivedSubscribers => DataReceived?.GetInvocationList().Length ?? 0;
        public int DisconnectedSubscribers => Disconnected?.GetInvocationList().Length ?? 0;
        public int ErrorSubscribers => Error?.GetInvocationList().Length ?? 0;

        public void RaiseDataReceived(byte[] data) => DataReceived?.Invoke(this, data);
        public void RaiseDisconnected() => Disconnected?.Invoke(this, EventArgs.Empty);
        public void RaiseError(Exception ex) => Error?.Invoke(this, ex);

        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable) { }
        public void UpdateActivity() { }
        public bool IsTimedOut(TimeSpan timeout) => false;
        public Task CloseAsync(string reason = "Normal") => Task.CompletedTask;
        public void Dispose() { }
    }
}

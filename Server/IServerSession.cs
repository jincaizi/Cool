using System;
using System.Threading.Tasks;
using Cool.Protocol;

namespace KcpServer
{
    /// <summary>
    /// 服务端会话契约 —— 传输无关。
    /// 业务代码（Server3C 主循环 / 各 Handler / AOI）只依赖本接口，
    /// 因此 KCP(UDP) 与 WebSocket 两条传输可以汇入同一个 ProcessPacket。
    /// 成员取自 KcpServerSession 的既有公开面，KCP 专属成员（SendSocket / KcpConv /
    /// SetSendSocket / EnsureWired / HandleUdpPacket / QueuedSendCount / DroppedSendCount）
    /// 刻意不进接口。
    /// </summary>
    public interface IServerSession : IDisposable
    {
        long SessionId { get; }
        bool IsConnected { get; }
        DateTime LastActivityTime { get; }

        event EventHandler<byte[]>? DataReceived;
        event EventHandler? Disconnected;
        event EventHandler<Exception>? Error;

        void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable);
        void UpdateActivity();
        bool IsTimedOut(TimeSpan timeout);
        Task CloseAsync(string reason = "Normal");
    }
}

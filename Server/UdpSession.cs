using System;
using System.Net;

namespace KcpServer
{
    /// <summary>
    /// UDP会话
    /// </summary>
    public sealed class UdpSession
    {
        public long SessionId { get; }
        public EndPoint RemoteEndPoint { get; }
        public bool IsConnected { get; private set; } = true;
        public DateTime ConnectedAt { get; }
        public DateTime LastActivityTime { get; set; }

        public UdpSession(long sessionId, EndPoint remoteEndPoint)
        {
            SessionId = sessionId;
            RemoteEndPoint = remoteEndPoint;
            ConnectedAt = DateTime.UtcNow;
            LastActivityTime = DateTime.UtcNow;
        }

        public void Disconnect()
        {
            IsConnected = false;
        }

        public void UpdateActivity()
        {
            LastActivityTime = DateTime.UtcNow;
        }
    }
}

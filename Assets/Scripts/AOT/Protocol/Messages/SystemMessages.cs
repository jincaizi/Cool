using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>心跳消息</summary>
    [ProtoContract]
    public class Heartbeat : IMessage
    {
        /// <summary>客户端时间戳</summary>
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }

        /// <summary>序列号</summary>
        [ProtoMember(2)]
        public uint Sequence { get; set; }
    }

    /// <summary>踢出消息（服务器 → 客户端）</summary>
    [ProtoContract]
    public class Kick : IMessage
    {
        /// <summary>原因</summary>
        [ProtoMember(1)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>踢出类型（0=正常踢出，1=违规，2=服务器维护）</summary>
        [ProtoMember(2)]
        public int KickType { get; set; }

        /// <summary>允许重连时间（秒，0=不允许重连）</summary>
        [ProtoMember(3)]
        public int AllowReconnectAfter { get; set; }
    }

    /// <summary>断开连接请求（客户端 → 服务器）</summary>
    [ProtoContract]
    public class DisconnectRequest : IMessage
    {
        /// <summary>原因</summary>
        [ProtoMember(1)]
        public string Reason { get; set; } = "ClientDisconnect";

        /// <summary>是否优雅断开</summary>
        [ProtoMember(2)]
        public bool Graceful { get; set; } = true;
    }

    /// <summary>关闭通知（服务器 → 客户端）</summary>
    [ProtoContract]
    public class ShutdownNotification : IMessage
    {
        /// <summary>延迟秒数</summary>
        [ProtoMember(1)]
        public int DelaySeconds { get; set; }

        /// <summary>原因</summary>
        [ProtoMember(2)]
        public string Reason { get; set; } = "ServerMaintenance";

        /// <summary>预计恢复时间</summary>
        [ProtoMember(3)]
        public long EstimatedRecoveryTime { get; set; }
    }
}

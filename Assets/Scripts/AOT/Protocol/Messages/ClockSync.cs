using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 时钟同步请求（客户端 → 服务器）。
    /// ClientTimestamp 语义：发送时刻的**客户端墙钟**（唯一允许墙钟的例外消息——
    /// 时钟同步的目标正是测量墙钟与服务器时钟的偏移，见 spec D3）。
    /// </summary>
    [ProtoContract]
    public class ClockSyncRequest : IMessage
    {
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }
    }

    /// <summary>时钟同步响应（服务器 → 客户端）</summary>
    [ProtoContract]
    public class ClockSyncResponse : IMessage
    {
        /// <summary>原样回传请求中的 ClientTimestamp</summary>
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }

        /// <summary>服务器时钟（响应生成时刻）</summary>
        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }
    }
}

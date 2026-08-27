using System;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>聊天消息</summary>
    [ProtoContract]
    public class ChatMessage : IMessage
    {
        /// <summary>发送者 ID</summary>
        [ProtoMember(1)]
        public long SenderId { get; set; }

        /// <summary>发送者名称</summary>
        [ProtoMember(2)]
        public string SenderName { get; set; } = string.Empty;

        /// <summary>消息内容</summary>
        [ProtoMember(3)]
        public string Content { get; set; } = string.Empty;

        /// <summary>频道类型（0=世界，1=私聊，2=队伍，3=公会）</summary>
        [ProtoMember(4)]
        public int ChannelType { get; set; }

        /// <summary>目标 ID（私聊时使用）</summary>
        [ProtoMember(5)]
        public long TargetId { get; set; }

        /// <summary>时间戳</summary>
        [ProtoMember(6)]
        public long Timestamp { get; set; }
    }

    /// <summary>系统广播消息</summary>
    [ProtoContract]
    public class BroadcastMessage : IMessage
    {
        /// <summary>消息类型</summary>
        [ProtoMember(1)]
        public int MessageType { get; set; }

        /// <summary>消息内容</summary>
        [ProtoMember(2)]
        public string Content { get; set; } = string.Empty;

        /// <summary>参数列表</summary>
        [ProtoMember(3)]
        public string[] Parameters { get; set; } = Array.Empty<string>();

        /// <summary>优先级（0=低，1=中，2=高）</summary>
        [ProtoMember(4)]
        public int Priority { get; set; }
    }
}

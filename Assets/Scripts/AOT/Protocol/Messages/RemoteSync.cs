using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>远程玩家位置同步（服务器 → 客户端广播）</summary>
    [ProtoContract]
    public class RemotePlayerSync : IMessage
    {
        /// <summary>玩家 ID</summary>
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>位置 X</summary>
        [ProtoMember(2)]
        public float X { get; set; }

        /// <summary>位置 Y</summary>
        [ProtoMember(3)]
        public float Y { get; set; }

        /// <summary>位置 Z</summary>
        [ProtoMember(4)]
        public float Z { get; set; }

        /// <summary>旋转角度（Y轴）</summary>
        [ProtoMember(5)]
        public float Rotation { get; set; }

        /// <summary>移动速度</summary>
        [ProtoMember(6)]
        public float Speed { get; set; }

        /// <summary>时间戳（服务器时钟语义）</summary>
        [ProtoMember(7)]
        public long Timestamp { get; set; }

        /// <summary>序列号</summary>
        [ProtoMember(8)]
        public uint Sequence { get; set; }
    }
}

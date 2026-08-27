using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>位置同步请求（B+ 模型：客户端报位置 + 序列号对账）</summary>
    [ProtoContract]
    public class PositionSyncRequest : IMessage
    {
        /// <summary>位置 X</summary>
        [ProtoMember(1)]
        public float X { get; set; }

        /// <summary>位置 Y</summary>
        [ProtoMember(2)]
        public float Y { get; set; }

        /// <summary>位置 Z</summary>
        [ProtoMember(3)]
        public float Z { get; set; }

        /// <summary>旋转角度（Y轴欧拉角）</summary>
        [ProtoMember(4)]
        public float Rotation { get; set; }

        /// <summary>速度</summary>
        [ProtoMember(5)]
        public float Speed { get; set; }

        /// <summary>时间戳（服务器时钟语义，spec §3）</summary>
        [ProtoMember(6)]
        public long Timestamp { get; set; }

        /// <summary>序列号（单调递增，用于 ack 对账）</summary>
        [ProtoMember(7)]
        public uint Sequence { get; set; }
    }

    /// <summary>位置同步响应（含权威纠偏数据）</summary>
    [ProtoContract]
    public class PositionSyncResponse : IMessage
    {
        /// <summary>确认的序列号</summary>
        [ProtoMember(1)]
        public uint AcknowledgedSequence { get; set; }

        /// <summary>服务器时间戳</summary>
        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }

        /// <summary>权威位置 X（偏差过大时下发）</summary>
        [ProtoMember(3)]
        public float AuthoritativeX { get; set; }

        /// <summary>权威位置 Y</summary>
        [ProtoMember(4)]
        public float AuthoritativeY { get; set; }

        /// <summary>权威位置 Z</summary>
        [ProtoMember(5)]
        public float AuthoritativeZ { get; set; }

        /// <summary>权威旋转角度</summary>
        [ProtoMember(6)]
        public float AuthoritativeRotation { get; set; }

        /// <summary>是否有位置校正（true 则客户端平滑修正到权威位置）</summary>
        [ProtoMember(7)]
        public bool HasPositionCorrection { get; set; }
    }
}

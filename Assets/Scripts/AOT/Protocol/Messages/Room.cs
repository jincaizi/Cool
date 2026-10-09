using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>玩家进入房间通知（服务器 → 其他客户端）</summary>
    [ProtoContract]
    public class PlayerEnterRoom : IMessage
    {
        /// <summary>玩家 ID</summary>
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>玩家名称</summary>
        [ProtoMember(2)]
        public string PlayerName { get; set; } = string.Empty;

        /// <summary>区服 ID</summary>
        [ProtoMember(3)]
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>出生位置 X</summary>
        [ProtoMember(4)]
        public float X { get; set; }

        /// <summary>出生位置 Y</summary>
        [ProtoMember(5)]
        public float Y { get; set; }

        /// <summary>出生位置 Z</summary>
        [ProtoMember(6)]
        public float Z { get; set; }

        /// <summary>朝向</summary>
        [ProtoMember(7)]
        public float Rotation { get; set; }

        /// <summary>服务器时间戳</summary>
        [ProtoMember(8)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>玩家离开房间通知（服务器 → 其他客户端）</summary>
    [ProtoContract]
    public class PlayerLeaveRoom : IMessage
    {
        /// <summary>玩家 ID</summary>
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>离开原因</summary>
        [ProtoMember(2)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>服务器时间戳</summary>
        [ProtoMember(3)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>房间全量快照（登录成功后下发）</summary>
    [ProtoContract]
    public class RoomSync : IMessage
    {
        /// <summary>区服 ID</summary>
        [ProtoMember(1)]
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>
        /// 房间内其他玩家快照列表。必须声明为 IList——List&lt;T&gt; 在 IL2CPP 上触发
        /// protobuf-net tuple 探测的 icall 缺口（见 ProtocolShapeGuardTests）。
        /// </summary>
        [ProtoMember(2)]
        public IList<RemotePlayerSync> Players { get; set; } = new List<RemotePlayerSync>();

        /// <summary>服务器时间戳</summary>
        [ProtoMember(3)]
        public long ServerTimestamp { get; set; }
    }
}

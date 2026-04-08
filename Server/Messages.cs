using System;
using System.Collections.Generic;
using ProtoBuf;

namespace KcpServer
{
    /// <summary>
    /// 消息ID枚举
    /// </summary>
    public enum MessageId : ushort
    {
        /// <summary>
        /// 登录请求
        /// </summary>
        LoginRequest = 1,

        /// <summary>
        /// 登录响应
        /// </summary>
        LoginResponse = 2,

        /// <summary>
        /// 心跳
        /// </summary>
        Heartbeat = 3,

        /// <summary>
        /// 位置同步请求
        /// </summary>
        PositionSyncRequest = 100,

        /// <summary>
        /// 位置同步响应
        /// </summary>
        PositionSyncResponse = 101,

        /// <summary>
        /// 远程玩家位置同步
        /// </summary>
        RemotePlayerSync = 1000,

        /// <summary>
        /// 玩家进入房间
        /// </summary>
        PlayerEnterRoom = 1001,

        /// <summary>
        /// 玩家离开房间
        /// </summary>
        PlayerLeaveRoom = 1002,

        /// <summary>
        /// 房间同步
        /// </summary>
        RoomSync = 1003,

        // ========== NPC消息 (2000-2099) ==========

        /// <summary>
        /// NPC生成
        /// </summary>
        NpcSpawn = 2000,

        /// <summary>
        /// NPC销毁
        /// </summary>
        NpcDespawn = 2001,

        /// <summary>
        /// NPC位置同步
        /// </summary>
        NpcPosSync = 2002,

        /// <summary>
        /// NPC动画同步
        /// </summary>
        NpcAnimSync = 2003
    }

    // ========== 登录消息 ==========

    [ProtoContract]
    public class LoginRequest : IMessage
    {
        [ProtoMember(1)]
        public string Username { get; set; } = string.Empty;

        [ProtoMember(2)]
        public string ZoneId { get; set; } = "Global";

        [ProtoMember(3)]
        public float X { get; set; }

        [ProtoMember(4)]
        public float Y { get; set; }

        [ProtoMember(5)]
        public float Z { get; set; }
    }

    [ProtoContract]
    public class LoginResponse : IMessage
    {
        [ProtoMember(1)]
        public bool Success { get; set; }

        [ProtoMember(2)]
        public string Message { get; set; } = string.Empty;

        [ProtoMember(3)]
        public long PlayerId { get; set; }

        [ProtoMember(4)]
        public string ZoneId { get; set; } = string.Empty;

        [ProtoMember(5)]
        public long ServerTimestamp { get; set; }
    }

    // ========== 位置同步消息 ==========

    [ProtoContract]
    public class PositionSyncRequest : IMessage
    {
        [ProtoMember(1)]
        public float X { get; set; }

        [ProtoMember(2)]
        public float Y { get; set; }

        [ProtoMember(3)]
        public float Z { get; set; }

        [ProtoMember(4)]
        public float Rotation { get; set; }

        [ProtoMember(5)]
        public float Speed { get; set; }

        [ProtoMember(6)]
        public long Timestamp { get; set; }

        [ProtoMember(7)]
        public uint Sequence { get; set; }
    }

    [ProtoContract]
    public class PositionSyncResponse : IMessage
    {
        [ProtoMember(1)]
        public uint AcknowledgedSequence { get; set; }

        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }

        [ProtoMember(3)]
        public float AuthoritativeX { get; set; }

        [ProtoMember(4)]
        public float AuthoritativeY { get; set; }

        [ProtoMember(5)]
        public float AuthoritativeZ { get; set; }

        [ProtoMember(6)]
        public float AuthoritativeRotation { get; set; }

        [ProtoMember(7)]
        public bool HasPositionCorrection { get; set; }
    }

    // ========== 服务端->客户端消息 ==========

    [ProtoContract]
    public class RemotePlayerSync : IMessage
    {
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        [ProtoMember(2)]
        public float X { get; set; }

        [ProtoMember(3)]
        public float Y { get; set; }

        [ProtoMember(4)]
        public float Z { get; set; }

        [ProtoMember(5)]
        public float Rotation { get; set; }

        [ProtoMember(6)]
        public float Speed { get; set; }

        [ProtoMember(7)]
        public long Timestamp { get; set; }

        [ProtoMember(8)]
        public uint Sequence { get; set; }
    }

    [ProtoContract]
    public class PlayerEnterRoom : IMessage
    {
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        [ProtoMember(2)]
        public string PlayerName { get; set; } = string.Empty;

        [ProtoMember(3)]
        public string ZoneId { get; set; } = string.Empty;

        [ProtoMember(4)]
        public float X { get; set; }

        [ProtoMember(5)]
        public float Y { get; set; }

        [ProtoMember(6)]
        public float Z { get; set; }

        [ProtoMember(7)]
        public float Rotation { get; set; }

        [ProtoMember(8)]
        public long ServerTimestamp { get; set; }
    }

    [ProtoContract]
    public class PlayerLeaveRoom : IMessage
    {
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        [ProtoMember(2)]
        public string Reason { get; set; } = string.Empty;

        [ProtoMember(3)]
        public long ServerTimestamp { get; set; }
    }

    [ProtoContract]
    public class RoomSync : IMessage
    {
        [ProtoMember(1)]
        public string ZoneId { get; set; } = string.Empty;

        [ProtoMember(2)]
        public List<RemotePlayerSync> Players { get; set; } = new List<RemotePlayerSync>();

        [ProtoMember(3)]
        public long ServerTimestamp { get; set; }
    }

    // ========== 心跳消息 ==========

    [ProtoContract]
    public class Heartbeat : IMessage
    {
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }

        [ProtoMember(2)]
        public uint Sequence { get; set; }
    }

    // ========== NPC消息 ==========

    /// <summary>
    /// NPC动画状态
    /// </summary>
    public enum NpcAnimationState : byte
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Attack = 3,
        Skill = 4,
        Hit = 5,
        Death = 6
    }

    [ProtoContract]
    public class NpcSpawn : IMessage
    {
        [ProtoMember(1)]
        public long NpcId { get; set; }

        [ProtoMember(2)]
        public string NpcTemplateId { get; set; } = string.Empty;

        [ProtoMember(3)]
        public float X { get; set; }

        [ProtoMember(4)]
        public float Y { get; set; }

        [ProtoMember(5)]
        public float Z { get; set; }

        [ProtoMember(6)]
        public float Rotation { get; set; }

        [ProtoMember(7)]
        public long ServerTimestamp { get; set; }
    }

    [ProtoContract]
    public class NpcDespawn : IMessage
    {
        [ProtoMember(1)]
        public long NpcId { get; set; }

        [ProtoMember(2)]
        public string Reason { get; set; } = string.Empty;

        [ProtoMember(3)]
        public long ServerTimestamp { get; set; }
    }

    [ProtoContract]
    public class NpcPosSync : IMessage
    {
        [ProtoMember(1)]
        public long NpcId { get; set; }

        [ProtoMember(2)]
        public float X { get; set; }

        [ProtoMember(3)]
        public float Y { get; set; }

        [ProtoMember(4)]
        public float Z { get; set; }

        [ProtoMember(5)]
        public float Rotation { get; set; }

        [ProtoMember(6)]
        public float Speed { get; set; }

        [ProtoMember(7)]
        public long Timestamp { get; set; }

        [ProtoMember(8)]
        public uint Sequence { get; set; }
    }

    [ProtoContract]
    public class NpcAnimSync : IMessage
    {
        [ProtoMember(1)]
        public long NpcId { get; set; }

        [ProtoMember(2)]
        public NpcAnimationState State { get; set; }

        [ProtoMember(3)]
        public int StateHash { get; set; }

        [ProtoMember(4)]
        public float TransitionDuration { get; set; }

        [ProtoMember(5)]
        public long Timestamp { get; set; }
    }
}
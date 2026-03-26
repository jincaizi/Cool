using System;
using ProtoBuf;

namespace KcpNet
{
    /// <summary>
    /// 消息基接口
    /// </summary>
    public interface IMessage
    {
    }

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
        /// 踢出
        /// </summary>
        Kick = 4,

        /// <summary>
        /// 断开连接请求
        /// </summary>
        DisconnectRequest = 5,

        /// <summary>
        /// 关闭通知
        /// </summary>
        ShutdownNotification = 6,

        /// <summary>
        /// 位置同步请求
        /// </summary>
        PositionSyncRequest = 100,

        /// <summary>
        /// 位置同步响应
        /// </summary>
        PositionSyncResponse = 101,

        /// <summary>
        /// 聊天消息
        /// </summary>
        ChatMessage = 200,

        /// <summary>
        /// 广播消息
        /// </summary>
        BroadcastMessage = 201
    }

    // ========== 基础消息类型 ==========

    /// <summary>
    /// 登录请求消息
    /// </summary>
    [ProtoContract]
    public class LoginRequest : IMessage
    {
        /// <summary>
        /// 用户名
        /// </summary>
        [ProtoMember(1)]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 密码（MD5哈希）
        /// </summary>
        [ProtoMember(2)]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>
        /// 客户端版本
        /// </summary>
        [ProtoMember(3)]
        public string ClientVersion { get; set; } = "1.0.0";

        /// <summary>
        /// 设备信息
        /// </summary>
        [ProtoMember(4)]
        public string DeviceInfo { get; set; } = string.Empty;
    }

    /// <summary>
    /// 登录响应消息
    /// </summary>
    [ProtoContract]
    public class LoginResponse : IMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [ProtoMember(1)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [ProtoMember(2)]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 用户ID
        /// </summary>
        [ProtoMember(3)]
        public long UserId { get; set; }

        /// <summary>
        /// 会话令牌
        /// </summary>
        [ProtoMember(4)]
        public string SessionToken { get; set; } = string.Empty;

        /// <summary>
        /// 服务器时间戳
        /// </summary>
        [ProtoMember(5)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>
    /// 心跳消息
    /// </summary>
    [ProtoContract]
    public class Heartbeat : IMessage
    {
        /// <summary>
        /// 客户端时间戳
        /// </summary>
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }

        /// <summary>
        /// 序列号
        /// </summary>
        [ProtoMember(2)]
        public uint Sequence { get; set; }
    }

    /// <summary>
    /// 踢出消息
    /// </summary>
    [ProtoContract]
    public class Kick : IMessage
    {
        /// <summary>
        /// 原因
        /// </summary>
        [ProtoMember(1)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// 踢出类型（0=正常踢出，1=违规，2=服务器维护）
        /// </summary>
        [ProtoMember(2)]
        public int KickType { get; set; }

        /// <summary>
        /// 允许重连时间（秒，0=不允许重连）
        /// </summary>
        [ProtoMember(3)]
        public int AllowReconnectAfter { get; set; }
    }

    /// <summary>
    /// 断开连接请求
    /// </summary>
    [ProtoContract]
    public class DisconnectRequest : IMessage
    {
        /// <summary>
        /// 原因
        /// </summary>
        [ProtoMember(1)]
        public string Reason { get; set; } = "ClientDisconnect";

        /// <summary>
        /// 是否优雅断开
        /// </summary>
        [ProtoMember(2)]
        public bool Graceful { get; set; } = true;
    }

    /// <summary>
    /// 关闭通知
    /// </summary>
    [ProtoContract]
    public class ShutdownNotification : IMessage
    {
        /// <summary>
        /// 延迟秒数
        /// </summary>
        [ProtoMember(1)]
        public int DelaySeconds { get; set; }

        /// <summary>
        /// 原因
        /// </summary>
        [ProtoMember(2)]
        public string Reason { get; set; } = "ServerMaintenance";

        /// <summary>
        /// 预计恢复时间
        /// </summary>
        [ProtoMember(3)]
        public long EstimatedRecoveryTime { get; set; }
    }

    // ========== 游戏相关消息类型 ==========

    /// <summary>
    /// 位置同步请求
    /// </summary>
    [ProtoContract]
    public class PositionSyncRequest : IMessage
    {
        /// <summary>
        /// 位置X
        /// </summary>
        [ProtoMember(1)]
        public float X { get; set; }

        /// <summary>
        /// 位置Y
        /// </summary>
        [ProtoMember(2)]
        public float Y { get; set; }

        /// <summary>
        /// 位置Z
        /// </summary>
        [ProtoMember(3)]
        public float Z { get; set; }

        /// <summary>
        /// 旋转角度
        /// </summary>
        [ProtoMember(4)]
        public float Rotation { get; set; }

        /// <summary>
        /// 速度
        /// </summary>
        [ProtoMember(5)]
        public float Speed { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        [ProtoMember(6)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 序列号
        /// </summary>
        [ProtoMember(7)]
        public uint Sequence { get; set; }
    }

    /// <summary>
    /// 位置同步响应
    /// </summary>
    [ProtoContract]
    public class PositionSyncResponse : IMessage
    {
        /// <summary>
        /// 确认的序列号
        /// </summary>
        [ProtoMember(1)]
        public uint AcknowledgedSequence { get; set; }

        /// <summary>
        /// 服务器时间戳
        /// </summary>
        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>
    /// 聊天消息
    /// </summary>
    [ProtoContract]
    public class ChatMessage : IMessage
    {
        /// <summary>
        /// 发送者ID
        /// </summary>
        [ProtoMember(1)]
        public long SenderId { get; set; }

        /// <summary>
        /// 发送者名称
        /// </summary>
        [ProtoMember(2)]
        public string SenderName { get; set; } = string.Empty;

        /// <summary>
        /// 消息内容
        /// </summary>
        [ProtoMember(3)]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 频道类型（0=世界，1=私聊，2=队伍，3=公会）
        /// </summary>
        [ProtoMember(4)]
        public int ChannelType { get; set; }

        /// <summary>
        /// 目标ID（私聊时使用）
        /// </summary>
        [ProtoMember(5)]
        public long TargetId { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        [ProtoMember(6)]
        public long Timestamp { get; set; }
    }

    /// <summary>
    /// 广播消息
    /// </summary>
    [ProtoContract]
    public class BroadcastMessage : IMessage
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        [ProtoMember(1)]
        public int MessageType { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [ProtoMember(2)]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 参数列表
        /// </summary>
        [ProtoMember(3)]
        public string[] Parameters { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 优先级（0=低，1=中，2=高）
        /// </summary>
        [ProtoMember(4)]
        public int Priority { get; set; }
    }
}
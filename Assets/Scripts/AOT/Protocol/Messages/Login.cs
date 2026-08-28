using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 登录请求（v1 合并版：原 KcpNet 版与 KcpServer 版字段并集；
    /// 丢弃服务端旧版 X/Y/Z 出生点字段，出生点由 P1 EnterWorld 承担）
    /// </summary>
    [ProtoContract]
    public class LoginRequest : IMessage
    {
        /// <summary>用户名</summary>
        [ProtoMember(1)]
        public string Username { get; set; } = string.Empty;

        /// <summary>密码（MD5哈希）</summary>
        [ProtoMember(2)]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>目标区服</summary>
        [ProtoMember(3)]
        public string ZoneId { get; set; } = "Global";

        /// <summary>客户端版本</summary>
        [ProtoMember(4)]
        public string ClientVersion { get; set; } = "1.0.0";

        /// <summary>设备信息</summary>
        [ProtoMember(5)]
        public string DeviceInfo { get; set; } = string.Empty;

        /// <summary>协议版本（握手校验，来自 ProtocolConstants.ProtocolVersion）</summary>
        [ProtoMember(6)]
        public long ProtocolVersion { get; set; }
    }

    /// <summary>
    /// 登录响应（v1 重排：PlayerId 吸收原 UserId 语义）
    /// </summary>
    [ProtoContract]
    public class LoginResponse : IMessage
    {
        /// <summary>是否成功</summary>
        [ProtoMember(1)]
        public bool Success { get; set; }

        /// <summary>结果描述</summary>
        [ProtoMember(2)]
        public string Message { get; set; } = string.Empty;

        /// <summary>玩家 ID（原 Client 版 UserId 与 Server 版 PlayerId 合并语义）</summary>
        [ProtoMember(3)]
        public long PlayerId { get; set; }

        /// <summary>会话令牌（断线重连用）</summary>
        [ProtoMember(4)]
        public string SessionToken { get; set; } = string.Empty;

        /// <summary>分配到的区服</summary>
        [ProtoMember(5)]
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>服务器时间戳（时间戳语义：发送时刻的服务器时钟，spec §3）</summary>
        [ProtoMember(6)]
        public long ServerTimestamp { get; set; }

        /// <summary>服务器 tick 间隔（毫秒；客户端据此换算插值窗口，spec D1）</summary>
        [ProtoMember(7)]
        public long ServerTickRateMs { get; set; }

        /// <summary>出生点 X（EnterWorld 语义并入登录响应，决策 D1）</summary>
        [ProtoMember(8)]
        public float SpawnX { get; set; }

        /// <summary>出生点 Y</summary>
        [ProtoMember(9)]
        public float SpawnY { get; set; }

        /// <summary>出生点 Z</summary>
        [ProtoMember(10)]
        public float SpawnZ { get; set; }

        /// <summary>出生朝向（Y 轴欧拉角）</summary>
        [ProtoMember(11)]
        public float SpawnRotation { get; set; }
    }
}

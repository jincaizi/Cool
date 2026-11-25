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

        /// <summary>
        /// 是否处于防御姿态（客户端上报；服务端只做几何 + 耐久校验，信任面登记见
        /// CombatRules.IsBlocked——与"客户端权威位置 + 服务端部分校验"同一哲学，不是防作弊闸门）。
        /// 累积式协议：只加不删——新字段一律追加高位 tag，既有字段号不改不挪；
        /// 缺该字段的老客户端读到 false = 未防御（服务端按全额伤害裁决，安全方向）
        /// </summary>
        [ProtoMember(8)]
        public bool IsDefending { get; set; }

        [ProtoMember(9)] public uint? RespawnTick { get; set; }
        /// <summary>水平运动模式（Plan 03；缺省 0 = Walk，旧客户端不会发送）。</summary>
        [ProtoMember(10)] public LocomotionMode LocomotionMode { get; set; }

        /// <summary>跳跃阶段（Start 只在每个新 JumpSeq 出现一次）。</summary>
        [ProtoMember(11)] public PlayerJumpPhase JumpPhase { get; set; }

        /// <summary>起跳序号：服务端按首次收到的新序号记录起跳 tick；重复 Start 不重置。</summary>
        [ProtoMember(12)] public uint JumpSeq { get; set; }

        /// <summary>垂直速度（只用于区分上升/下落，不参与预算）。</summary>
        [ProtoMember(13)] public float VerticalVelocity { get; set; }

        /// <summary>正在释放的位移技能 ID；0 = 无位移技能。</summary>
        [ProtoMember(14)] public int DisplacementSkillId { get; set; }

        /// <summary>位移技能对应的已确认 CastId；0 = 未确认/非位移。</summary>
        [ProtoMember(15)] public ulong CastId { get; set; }

        /// <summary>客户端最后应用的控制版本；服务端拒绝低于当前权威版本的包。</summary>
        [ProtoMember(16)] public uint ClientControlVersion { get; set; }

        /// <summary>生命代次；非 0 且与服务端不一致时整包丢弃。</summary>
        [ProtoMember(17)] public uint LifeId { get; set; }

        /// <summary>WIP 战斗整改：移动代次（与 Plan 03 tag 冲突后顺延到 18）。</summary>
        [ProtoMember(18)] public uint MovementRevision { get; set; }
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

        [ProtoMember(8)] public uint? RespawnTick { get; set; }
        /// <summary>服务端当前控制版本（客户端据此识别旧输入；解除以 CombatStateUpdate 为准）。</summary>
        [ProtoMember(9)] public uint ControlVersion { get; set; }

        /// <summary>WIP 战斗整改：服务端确认的移动代次（顺延到 10）。</summary>
        [ProtoMember(10)] public uint MovementRevision { get; set; }
    }
}

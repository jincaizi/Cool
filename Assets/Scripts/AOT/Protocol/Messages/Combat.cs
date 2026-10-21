using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 技能命中形状（spec §5-P4：判定几何两端同源）——数值契约：
    /// ShapeType 与客户端 ShapeBlock.TargetType 枚举同值（Single=0 / AOE_Circle=1 /
    /// AOE_Cone=2 / AOE_Sector=3），Angle 为全角。只加不删：新形状字段追加高位 tag；
    /// 客户端枚举另有 Self=4（P4 不用）。内嵌类型非线上消息，实现标记接口仅为协议层惯例
    /// </summary>
    [ProtoContract]
    public class HitShape : IMessage
    {
        [ProtoMember(1)] public int ShapeType { get; set; }
        [ProtoMember(2)] public float Range { get; set; }
        [ProtoMember(3)] public float Angle { get; set; }
        [ProtoMember(4)] public float AngleStart { get; set; }
        [ProtoMember(5)] public float AngleEnd { get; set; }
        [ProtoMember(6)] public float InnerRadius { get; set; }
        [ProtoMember(7)] public float InnerAngle { get; set; }
        [ProtoMember(8)] public float AreaRadius { get; set; }
    }

    /// <summary>施法请求（客户端 → 服务器）。形状随包上线；服务端按 CombatRules 表校验上限（防篡改）。</summary>
    [ProtoContract]
    public class CastRequest : IMessage
    {
        [ProtoMember(1)] public int SkillId { get; set; }

        /// <summary>锁定目标（P4 客户端恒 0——服务端 Single 形状自动选最近前方实体，决策 D-P4-2）</summary>
        [ProtoMember(2)] public long TargetPlayerId { get; set; }

        [ProtoMember(3)] public HitShape Shape { get; set; } = new HitShape();

        /// <summary>落点（仅 CircleAtPoint 类技能使用：20004/30001）。服务端校验有限性与距施法者上限。</summary>
        [ProtoMember(4)] public float ImpactX { get; set; }
        [ProtoMember(5)] public float ImpactY { get; set; }
        [ProtoMember(6)] public float ImpactZ { get; set; }

        /// <summary>蓄力进度 [0,1]：20004 用于半径/伤害缩放；20003 释放时也随包发送
        /// （服务端忽略，仅 20004 的 ChargeScales 消费）。服务端钳制，NaN 视为 0。</summary>
        [ProtoMember(7)] public float ChargeProgress { get; set; }
    }

    /// <summary>施法广播（服务器 → caster 及视野内观察者；远程表现驱动，P4 表现占位）</summary>
    [ProtoContract]
    public class CastBroadcast : IMessage
    {
        [ProtoMember(1)] public int SkillId { get; set; }
        [ProtoMember(2)] public long CasterPlayerId { get; set; }
        [ProtoMember(3)] public long TargetPlayerId { get; set; }
        [ProtoMember(4)] public float CasterX { get; set; }
        [ProtoMember(5)] public float CasterY { get; set; }
        [ProtoMember(6)] public float CasterZ { get; set; }
        [ProtoMember(7)] public float CasterRotation { get; set; }
        [ProtoMember(8)] public HitShape Shape { get; set; } = new HitShape();
        [ProtoMember(9)] public long ServerTimestamp { get; set; }
    }

    /// <summary>单次命中结果（D4：战斗骰只有服务器掷——伤害值随消息下发，客户端不重算）。内嵌类型非线上消息，实现标记接口仅为协议层惯例</summary>
    [ProtoContract]
    public class HitEntry : IMessage
    {
        [ProtoMember(1)] public long TargetPlayerId { get; set; }
        [ProtoMember(2)] public float Damage { get; set; }
        [ProtoMember(3)] public bool IsCritical { get; set; }

        /// <summary>扣血后目标剩余 HP（服务器权威）</summary>
        [ProtoMember(4)] public float TargetRemainingHp { get; set; }

        [ProtoMember(5)] public float HitX { get; set; }
        [ProtoMember(6)] public float HitZ { get; set; }

        /// <summary>
        /// 这一击被格挡了（服务端裁决；Damage 已按 CombatRules.BlockDamageScale 减免，
        /// TargetRemainingHp 仍是扣后权威余血）。客户端据此走格挡受击表现（本地玩家）。
        /// 累积式协议：只加不删——新字段追加高位 tag；缺该字段的老客户端读到 false = 普通受击
        /// </summary>
        [ProtoMember(7)] public bool IsBlocked { get; set; }
    }

    /// <summary>伤害结果（服务器 → caster + 每个受击者；双端伤害数字一致性载体）。AOE 多命中打包。</summary>
    [ProtoContract]
    public class DamageResult : IMessage
    {
        [ProtoMember(1)] public long SourcePlayerId { get; set; }
        [ProtoMember(2)] public int SkillId { get; set; }
        [ProtoMember(3)] public long ServerTimestamp { get; set; }
        /// <summary>必须声明为 IList——List&lt;T&gt; 在 IL2CPP 上触发 protobuf-net tuple 探测的 icall 缺口（见 ProtocolShapeGuardTests）</summary>
        [ProtoMember(4)] public IList<HitEntry> Hits { get; set; } = new List<HitEntry>();

        /// <summary>本次结果是否包含 LineAll 线形吸收（服务端只在剑气被格挡时置位）</summary>
        [ProtoMember(5)] public bool ProjectileStopped { get; set; }

        /// <summary>吸收点世界 X（ProjectileStopped=true 时有效）</summary>
        [ProtoMember(6)] public float StopX { get; set; }

        /// <summary>吸收点世界 Z（ProjectileStopped=true 时有效）</summary>
        [ProtoMember(7)] public float StopZ { get; set; }

        /// <summary>
        /// 来源种类（值同 EntityKind：0 Player / 1 Npc / 2 PlayerBot）。
        /// 只加不删：tag 8，老客户端读默认 0。客户端据此区分玩家来源闪屏与怪物攻击。
        /// </summary>
        [ProtoMember(8)] public int SourceKind { get; set; }
    }
}

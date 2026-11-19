using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>剑气回荡状态阶段（只加不删；1/2/3 是线上值）。</summary>
    public enum SwordQiEchoPhase
    {
        Rejected = 1,
        Started = 2,
        Ended = 3,
    }

    /// <summary>首次接触时间表中的一条：目标、负责剑气、相对施法起点的 tick 与位置快照。</summary>
    [ProtoContract]
    public class SwordQiEchoContactMessage : IMessage
    {
        [ProtoMember(1)] public long TargetId { get; set; }
        [ProtoMember(2)] public int SwordIndex { get; set; }
        [ProtoMember(3)] public uint OffsetTicks { get; set; }
        [ProtoMember(4)] public float X { get; set; }
        [ProtoMember(5)] public float Y { get; set; }
        [ProtoMember(6)] public float Z { get; set; }
    }

    /// <summary>
    /// 剑气回荡实例（服务器 → 施法者、目标玩家与同区已协商能力的观察者）。
    /// 开始/结束按 (连接代次, CastId) 去重；结束状态优先于同实例重放的开始。
    /// Contacts 必须声明为 IList（IL2CPP tuple 探测约束，见 ProtocolShapeGuardTests）。
    /// </summary>
    [ProtoContract]
    public class SwordQiEchoState : IMessage
    {
        [ProtoMember(1)] public SwordQiEchoPhase Phase { get; set; }
        [ProtoMember(2)] public long CasterId { get; set; }
        [ProtoMember(3)] public ulong CastId { get; set; }
        [ProtoMember(4)] public ulong RequestId { get; set; }
        [ProtoMember(5)] public ulong EchoBindingId { get; set; }
        [ProtoMember(6)] public string RejectReason { get; set; } = string.Empty;
        [ProtoMember(7)] public uint StartTick { get; set; }
        [ProtoMember(8)] public uint EndTick { get; set; }
        [ProtoMember(9)] public long ServerTimestamp { get; set; }
        [ProtoMember(10)] public uint CooldownTicks { get; set; }
        [ProtoMember(11)] public float OriginX { get; set; }
        [ProtoMember(12)] public float OriginY { get; set; }
        [ProtoMember(13)] public float OriginZ { get; set; }
        [ProtoMember(14)] public float Rotation { get; set; }
        [ProtoMember(15)] public float Radius { get; set; }
        [ProtoMember(16)] public int SwordCount { get; set; }
        [ProtoMember(17)] public float OvershootMeters { get; set; }
        [ProtoMember(18)] public IList<SwordQiEchoContactMessage> Contacts { get; set; }
            = new List<SwordQiEchoContactMessage>();
    }
}

using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>状态/装备/控制/活动施法的可靠完整投影；Plan 02 只填 HP/装备/活动施法。</summary>
    [ProtoContract]
    public class CombatStateUpdate : IMessage
    {
        [ProtoMember(1)] public long EntityId { get; set; }
        [ProtoMember(2)] public CombatProjection Projection { get; set; } = new CombatProjection();
        [ProtoMember(3)] public long ServerTimestamp { get; set; }
    }

    [ProtoContract]
    public class CombatProjection : IMessage
    {
        [ProtoMember(1)] public uint CombatVersion { get; set; }
        [ProtoMember(2)] public uint LifeId { get; set; }
        [ProtoMember(3)] public float Hp { get; set; }
        [ProtoMember(4)] public float MaxHp { get; set; }
        [ProtoMember(5)] public bool Dead { get; set; }
        [ProtoMember(6)] public bool IsDefending { get; set; }
        /// <summary>IList：IL2CPP tuple 探测缺口，禁止 List&lt;T&gt;。</summary>
        [ProtoMember(7)] public IList<StatusProjection> Statuses { get; set; } = new List<StatusProjection>();
        [ProtoMember(8)] public EquipmentSummary? Equipment { get; set; }
        [ProtoMember(9)] public ControlSummary? Control { get; set; }
        [ProtoMember(10)] public ActiveCastProjection? ActiveCast { get; set; }
    }

    [ProtoContract]
    public class StatusProjection : IMessage
    {
        [ProtoMember(1)] public int EffectId { get; set; }
        [ProtoMember(2)] public int GroupId { get; set; }
        [ProtoMember(3)] public uint EndTick { get; set; }
        [ProtoMember(4)] public long SourceId { get; set; }
        [ProtoMember(5)] public float SlowRatio { get; set; }
        [ProtoMember(6)] public float DamagePerTick { get; set; }
        [ProtoMember(7)] public uint IntervalTicks { get; set; }
        [ProtoMember(8)] public float IncomingDamageMultiplier { get; set; }
    }

    [ProtoContract]
    public class EquipmentSummary : IMessage
    {
        [ProtoMember(1)] public long WeaponInstanceId { get; set; }
        [ProtoMember(2)] public int TemplateId { get; set; }
        [ProtoMember(3)] public int ElementId { get; set; }
    }

    [ProtoContract]
    public class ControlSummary : IMessage
    {
        [ProtoMember(1)] public float SpeedScale { get; set; } = 1f;
        [ProtoMember(2)] public bool ActionLock { get; set; }
        [ProtoMember(3)] public uint ControlVersion { get; set; }
    }

    [ProtoContract]
    public class ActiveCastProjection : IMessage
    {
        [ProtoMember(1)] public ulong CastId { get; set; }
        [ProtoMember(2)] public int SkillId { get; set; }
        [ProtoMember(3)] public NpcActionPhase Phase { get; set; }
        [ProtoMember(4)] public uint EndTick { get; set; }
    }
}

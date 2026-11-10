using ProtoBuf;

namespace Cool.Protocol
{
    public static class ProtocolFeatures
    {
        public const uint NpcActionTimelineV1 = 1u;
        public const uint AuthoritativeRespawn = 1u << 1;
    }

    public enum NpcActionKind : byte { Idle = 0, Move = 1, Skill = 2, HitReaction = 3, Death = 4 }
    public enum NpcActionPhase : byte { Continuous = 0, Accepted = 1, Windup = 2, Active = 3, Recovery = 4, Completed = 5, Cancelled = 6 }
    public enum NpcActionCancelReason : byte { None = 0, Hit = 1, TargetInvalid = 2, Leash = 3, Death = 4, Forced = 5 }

    /// <summary>Frozen scalar action state. Tick zero is valid; intervals are half open.</summary>
    [ProtoContract]
    public class NpcActionSnapshot : IMessage
    {
        [ProtoMember(1)] public ulong InstanceId { get; set; }
        [ProtoMember(2)] public uint Revision { get; set; }
        [ProtoMember(3)] public NpcActionKind Kind { get; set; }
        [ProtoMember(4)] public NpcActionPhase Phase { get; set; }
        [ProtoMember(5)] public uint StartTick { get; set; }
        [ProtoMember(6)] public uint PhaseStartTick { get; set; }
        [ProtoMember(7)] public uint EndTick { get; set; }
        [ProtoMember(8)] public int SkillId { get; set; }
        [ProtoMember(9)] public long TargetId { get; set; }
        [ProtoMember(10)] public int PresentationId { get; set; }
        [ProtoMember(11)] public NpcActionCancelReason CancelReason { get; set; }
        [ProtoMember(12)] public uint ActiveStartTick { get; set; }
        [ProtoMember(13)] public uint ActiveEndTick { get; set; }
        [ProtoMember(14)] public uint RecoveryStartTick { get; set; }
        [ProtoMember(15)] public float AimX { get; set; }
        [ProtoMember(16)] public float AimY { get; set; }
        [ProtoMember(17)] public float AimZ { get; set; }
        [ProtoMember(18)] public float AimYaw { get; set; }
        [ProtoMember(19)] public int PresentationVariant { get; set; }
        [ProtoMember(20)] public uint EffectiveTick { get; set; }

        public NpcActionSnapshot Clone() => (NpcActionSnapshot)MemberwiseClone();
    }

    [ProtoContract]
    public class NpcActionEvent : IMessage
    {
        [ProtoMember(1)] public long EntityId { get; set; }
        [ProtoMember(2)] public NpcActionSnapshot Action { get; set; } = new NpcActionSnapshot();
        [ProtoMember(3)] public long ServerTimestamp { get; set; }
    }

    [ProtoContract]
    public class NpcVisibilityExit : IMessage
    {
        [ProtoMember(1)] public long EntityId { get; set; }
        [ProtoMember(2)] public uint ExitTick { get; set; }
    }
}

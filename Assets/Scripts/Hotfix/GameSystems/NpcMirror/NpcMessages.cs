using UnityEngine;
using ProtoBuf;
using System;

namespace Hotfix.GameSystems.NpcMirror
{
    [ProtoContract]
    public class NpcSpawn
    {
        [ProtoMember(1)] public long NpcId { get; set; }
        [ProtoMember(2)] public int NpcTemplateId { get; set; }
        [ProtoMember(3)] public float X { get; set; }
        [ProtoMember(4)] public float Y { get; set; }
        [ProtoMember(5)] public float Z { get; set; }
        [ProtoMember(6)] public float Rotation { get; set; }
        [ProtoMember(7)] public long ServerTimestamp { get; set; }

        public Vector3 Position => new Vector3(X, Y, Z);
        public Quaternion RotationQuat => Quaternion.Euler(0, Rotation, 0);
    }

    [ProtoContract]
    public class NpcDespawn
    {
        [ProtoMember(1)] public long NpcId { get; set; }
        [ProtoMember(2)] public string Reason { get; set; }
        [ProtoMember(3)] public long ServerTimestamp { get; set; }
    }

    [ProtoContract]
    public class NpcPosSync
    {
        [ProtoMember(1)] public long NpcId { get; set; }
        [ProtoMember(2)] public float X { get; set; }
        [ProtoMember(3)] public float Y { get; set; }
        [ProtoMember(4)] public float Z { get; set; }
        [ProtoMember(5)] public float Rotation { get; set; }
        [ProtoMember(6)] public float Speed { get; set; }
        [ProtoMember(7)] public long Timestamp { get; set; }
        [ProtoMember(8)] public uint Sequence { get; set; }

        public Vector3 Position => new Vector3(X, Y, Z);
        public Quaternion RotationQuat => Quaternion.Euler(0, Rotation, 0);
    }

    [ProtoContract]
    public class NpcAnimSync
    {
        [ProtoMember(1)] public long NpcId { get; set; }
        [ProtoMember(2)] public NpcAnimationState State { get; set; }
        [ProtoMember(3)] public int StateHash { get; set; }
        [ProtoMember(4)] public float TransitionDuration { get; set; }
        [ProtoMember(5)] public long Timestamp { get; set; }
    }

    public enum NpcAnimationState
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Attack = 3,
        Skill = 4,
        Hit = 5,
        Death = 6
    }
}

using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>
    /// 一次「剑气回荡」的服务端权威实例。接触锁定目标对象身份与生命代次；
    /// 到点时重查存活、区域、范围和生命，失效则跳过且不补排期。
    /// </summary>
    public sealed class SwordQiEchoCast
    {
        public sealed class Contact
        {
            public long TargetId;
            public PlayerState Target = null!;
            public uint TargetLifeId;
            public int SwordIndex;
            public uint OffsetTicks;
            public Vec3 Position;
            public bool Resolved;
        }

        public long CasterId;
        public PlayerState Caster = null!;
        public uint CasterLifeId;
        public string ZoneId = string.Empty;
        public ulong CastId;
        public ulong RequestId;
        public ulong BindingId;
        public uint StartTick;
        public uint EndTick;
        public uint CooldownTicks;
        public Vec3 Origin;
        public float Rotation;
        public float BaseDamage;
        public float CriticalRate;
        public CastBroadcast Broadcast = null!;
        public readonly List<Contact> Contacts = new List<Contact>();
        public readonly HashSet<long> Recipients = new HashSet<long>();
    }
}

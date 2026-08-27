using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>NPC 动画状态</summary>
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

    /// <summary>NPC 生成通知</summary>
    [ProtoContract]
    public class NpcSpawn : IMessage
    {
        /// <summary>NPC ID</summary>
        [ProtoMember(1)]
        public long NpcId { get; set; }

        /// <summary>NPC 模板 ID</summary>
        [ProtoMember(2)]
        public string NpcTemplateId { get; set; } = string.Empty;

        /// <summary>位置 X</summary>
        [ProtoMember(3)]
        public float X { get; set; }

        /// <summary>位置 Y</summary>
        [ProtoMember(4)]
        public float Y { get; set; }

        /// <summary>位置 Z</summary>
        [ProtoMember(5)]
        public float Z { get; set; }

        /// <summary>朝向</summary>
        [ProtoMember(6)]
        public float Rotation { get; set; }

        /// <summary>服务器时间戳</summary>
        [ProtoMember(7)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>NPC 销毁通知</summary>
    [ProtoContract]
    public class NpcDespawn : IMessage
    {
        /// <summary>NPC ID</summary>
        [ProtoMember(1)]
        public long NpcId { get; set; }

        /// <summary>原因</summary>
        [ProtoMember(2)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>服务器时间戳</summary>
        [ProtoMember(3)]
        public long ServerTimestamp { get; set; }
    }

    /// <summary>NPC 位置同步</summary>
    [ProtoContract]
    public class NpcPosSync : IMessage
    {
        /// <summary>NPC ID</summary>
        [ProtoMember(1)]
        public long NpcId { get; set; }

        /// <summary>位置 X</summary>
        [ProtoMember(2)]
        public float X { get; set; }

        /// <summary>位置 Y</summary>
        [ProtoMember(3)]
        public float Y { get; set; }

        /// <summary>位置 Z</summary>
        [ProtoMember(4)]
        public float Z { get; set; }

        /// <summary>朝向</summary>
        [ProtoMember(5)]
        public float Rotation { get; set; }

        /// <summary>速度</summary>
        [ProtoMember(6)]
        public float Speed { get; set; }

        /// <summary>时间戳</summary>
        [ProtoMember(7)]
        public long Timestamp { get; set; }

        /// <summary>序列号</summary>
        [ProtoMember(8)]
        public uint Sequence { get; set; }
    }

    /// <summary>NPC 动画同步</summary>
    [ProtoContract]
    public class NpcAnimSync : IMessage
    {
        /// <summary>NPC ID</summary>
        [ProtoMember(1)]
        public long NpcId { get; set; }

        /// <summary>动画状态</summary>
        [ProtoMember(2)]
        public NpcAnimationState State { get; set; }

        /// <summary>状态哈希</summary>
        [ProtoMember(3)]
        public int StateHash { get; set; }

        /// <summary>过渡时长</summary>
        [ProtoMember(4)]
        public float TransitionDuration { get; set; }

        /// <summary>时间戳</summary>
        [ProtoMember(5)]
        public long Timestamp { get; set; }
    }
}

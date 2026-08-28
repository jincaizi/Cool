using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 实体字段位掩码（增量快照编码，spec D5）——字段值仅在对应位被置位时有效。
    /// 只加不删：新增字段追加高位。
    /// </summary>
    [System.Flags]
    public enum EntityFieldMask : ushort
    {
        None = 0,
        Position = 1 << 0,
        Rotation = 1 << 1,
        Speed = 1 << 2,
        /// <summary>动作/战斗状态（攻击中/受击/眩晕…，spec F3a 首日含入；P1 恒为 0，P2 客户端上报后填充）</summary>
        ActionState = 1 << 3
    }

    /// <summary>实体快照（内嵌于 WorldSnapshot，不单独注册消息 ID）</summary>
    [ProtoContract]
    public class EntitySnapshot : IMessage
    {
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>本次快照携带哪些字段（EntityFieldMask 位图）</summary>
        [ProtoMember(2)]
        public ushort FieldMask { get; set; }

        [ProtoMember(3)]
        public float X { get; set; }

        [ProtoMember(4)]
        public float Y { get; set; }

        [ProtoMember(5)]
        public float Z { get; set; }

        [ProtoMember(6)]
        public float Rotation { get; set; }

        [ProtoMember(7)]
        public float Speed { get; set; }

        /// <summary>动作/战斗状态标志（0=无，语义 P2 定义）</summary>
        [ProtoMember(8)]
        public byte ActionState { get; set; }

        /// <summary>本快照对应的服务器 tick</summary>
        [ProtoMember(9)]
        public uint Tick { get; set; }
    }

    /// <summary>
    /// 世界快照广播（服务器 → 客户端，TickLoop 每 tick 组装）
    /// </summary>
    [ProtoContract]
    public class WorldSnapshot : IMessage
    {
        /// <summary>服务器 tick 序号</summary>
        [ProtoMember(1)]
        public uint Tick { get; set; }

        /// <summary>服务器时钟（组装时刻）</summary>
        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }

        /// <summary>本 tick 的脏实体快照（全静时为空列表）</summary>
        [ProtoMember(3)]
        public List<EntitySnapshot> Entities { get; set; } = new List<EntitySnapshot>();
    }
}

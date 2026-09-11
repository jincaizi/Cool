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
        /// <summary>动作/战斗状态（攻击中/受击/眩晕…，spec D5 首日含入；P1 恒为 0，P2 定义语义并填充）</summary>
        ActionState = 1 << 3,
        /// <summary>实体种类（EntityKind：0=Player/1=Npc）——P5 怪物入实体快照</summary>
        Kind = 1 << 4,
        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）——P5 客户端怪物渲染区分</summary>
        TemplateId = 1 << 5
    }

    /// <summary>
    /// 实体种类（EntitySnapshot.Kind）——P5 怪物服务端化。
    /// 0=Player（protobuf 默认，旧客户端兼容），1=Npc 怪物。
    /// </summary>
    public enum EntityKind : byte
    {
        Player = 0,
        Npc = 1
    }

    /// <summary>实体快照（内嵌于 WorldSnapshot，不单独注册消息 ID；内嵌类型非线上消息，实现标记接口仅为协议层惯例）</summary>
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

        /// <summary>实体种类（EntityKind；protobuf 默认 0=Player）</summary>
        [ProtoMember(10)]
        public byte Kind { get; set; }

        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）</summary>
        [ProtoMember(11)]
        public int TemplateId { get; set; }

        /// <summary>本快照对应的服务器 tick</summary>
        [ProtoMember(9)]
        public uint Tick { get; set; }
    }

    /// <summary>
    /// 世界快照广播（服务器 → 客户端，TickLoop 每 tick 组装）——
    /// P2 T3 起为唯一的位置广播通道（RemotePlayerSync/RoomSync 登录下发已退役，
    /// 仅解码兼容保留，勿新增消费方）。
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

        /// <summary>本 tick 的实体快照（脏实体全字段 + 静止实体 4Hz beacon；全静时本消息不发送）</summary>
        [ProtoMember(3)]
        public List<EntitySnapshot> Entities { get; set; } = new List<EntitySnapshot>();
    }
}

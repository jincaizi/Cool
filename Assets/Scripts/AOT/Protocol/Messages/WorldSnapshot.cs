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
        TemplateId = 1 << 5,
        /// <summary>
        /// 实体是否已死（D-N1：用 Dead 而非 Alive——protobuf 零默认让"活着"不占字节）。
        /// **每次快照都置位**（D-N2）：存活是**状态**不是事件，新观察者首帧就要知道；
        /// 旧客户端 `view.Dead = snapshot.Dead` 的无条件语义依赖本口径。
        /// </summary>
        Dead = 1 << 6,
        /// <summary>
        /// Hp / MaxHp 有效（**条件字段**：仅 Baseline 或血量翻转时置位）。
        /// 未置位时客户端沿用 last-known，不能用 protobuf 默认 0 覆盖。
        /// </summary>
        Health = 1 << 7,
        /// <summary>IsDefending 有效（**每次快照都置位**，与 Dead 同为状态而非事件）。</summary>
        Defending = 1 << 8,
        NpcAction = 1 << 9,
        /// <summary>状态/装备/控制/活动施法投影（仅 Baseline 与 CombatStateUpdate 使用；
        /// 与 Dead/Defending 无关——那两位每帧携带）。</summary>
        Combat = 1 << 10
    }

    /// <summary>
    /// 实体种类（EntitySnapshot.Kind）——P5 怪物服务端化。
    /// 0=Player（protobuf 默认，旧客户端兼容），1=Npc 怪物，
    /// 2=PlayerBot 压测 bot（走玩家的伤害路径；客户端渲染**与真人同一条真模型路径**——
    /// D-P5-7 的"渲染成占位"已被实测推翻：AOI 下的同屏数由局部密度决定，见 RemoteRenderBranchTests）。
    /// **0/1 是已发布的 wire 契约，只加不删。**
    /// </summary>
    public enum EntityKind : byte
    {
        Player = 0,
        Npc = 1,
        PlayerBot = 2
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

        /// <summary>实体是否已死（服务器权威；FieldMask.Dead 每帧置位，仅在缺失该位的旧服务端
        /// 快照上无效——此时客户端沿用 last-known）</summary>
        [ProtoMember(12)]
        public bool Dead { get; set; }

        /// <summary>服务端权威血量（条件字段：FieldMask.Health 置位时有效；未置位沿用 last-known）</summary>
        [ProtoMember(13)] public float Hp { get; set; }
        /// <summary>服务端权威最大血量（条件字段：FieldMask.Health 置位时有效；未置位沿用 last-known）</summary>
        [ProtoMember(14)] public float MaxHp { get; set; }
        /// <summary>是否防御中（FieldMask.Defending 每帧置位）</summary>
        [ProtoMember(15)] public bool IsDefending { get; set; }
        [ProtoMember(16)] public NpcActionSnapshot? NpcAction { get; set; }
        [ProtoMember(17)] public IList<NpcActionSnapshot> NpcActionHistory { get; set; } = new List<NpcActionSnapshot>();

        /// <summary>实体生命代次（NpcAction 合并键；Part A 随每次 AOI 快照携带，Part B 收窄为基线/Health 快照）</summary>
        [ProtoMember(19)] public uint LifeId { get; set; }

        /// <summary>实体战斗版本（Combat 投影的单调版本戳）</summary>
        [ProtoMember(18)] public uint CombatVersion { get; set; }

        /// <summary>战斗投影（仅基线快照与 FieldMask.Combat 置位时有效）</summary>
        [ProtoMember(20)] public CombatProjection? Combat { get; set; }
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

        /// <summary>
        /// 本 tick 的实体快照（脏实体全字段 + 静止实体 4Hz beacon；全静时本消息不发送）。
        /// 必须声明为 IList：List&lt;T&gt; 会让 protobuf-net 的 tuple 探测在 IL2CPP 上抛
        /// NotSupportedException（解码静默失败），门禁见 ProtocolShapeGuardTests。
        /// </summary>
        [ProtoMember(3)]
        public IList<EntitySnapshot> Entities { get; set; } = new List<EntitySnapshot>();
        [ProtoMember(4)] public IList<NpcVisibilityExit> NpcExits { get; set; } = new List<NpcVisibilityExit>();
        [ProtoMember(5)] public ulong SnapshotSequence { get; set; }
        [ProtoMember(6)] public int PartIndex { get; set; }
        [ProtoMember(7)] public int PartCount { get; set; }
        /// <summary>Shared simulation sample anchor; ServerTimestamp remains the send time.</summary>
        [ProtoMember(8)] public long TickAnchorServerTimestamp { get; set; }
    }
}

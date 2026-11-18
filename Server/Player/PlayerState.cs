using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>
    /// 玩家权威状态
    /// </summary>
    public sealed class PlayerState
    {
        public long PlayerId { get; }
        public string PlayerName { get; set; } = string.Empty;
        public string ZoneId { get; set; } = "Global";
        public Vector3 Position { get; internal set; }
        public float Rotation { get; internal set; }
        public float Speed { get; internal set; }
        public long LastUpdateTimestamp { get; internal set; }
        public uint LastReceivedSequence { get; internal set; }
        internal bool SupportsAuthoritativeRespawn { get; set; }
        internal uint? RespawnTick { get; set; }

        /// <summary>P4 战斗状态（服务器权威；登录重建 PlayerState 即满血——复活另见 D-2 的 8 秒计时）</summary>
        public float Hp { get; internal set; } = 100f;
        public float MaxHp { get; internal set; } = 100f;
        public bool IsAlive => Hp > 0f;
        private readonly System.Collections.Generic.Dictionary<int, uint> _lastCastTicks =
            new System.Collections.Generic.Dictionary<int, uint>();

        /// <summary>服务器权威扣血（命中裁决调用；可到负值——死亡判定唯一依据 IsAlive，复活见 D-2）</summary>
        public void ApplyDamage(float damage)
        {
            Hp -= damage;
            BumpCombatVersion();
        }

        /// <summary>
        /// 防御姿态（客户端经 PositionSyncRequest.IsDefending 上报，每次位置同步刷新——
        /// PositionSyncHandler；**不进 MovementValidator**：位置校验与防御无关）。
        /// 服务端只做姿态 + 几何判断；盾资源只由客户端精力控制（2026-10-21 删除耐久）。
        /// </summary>
        public bool IsDefending { get; internal set; }

        /// <summary>死亡发生的 tick（0 = 活着）。复活计时从它起算（D-2：8 秒）</summary>
        public long DeadTick { get; private set; }

        /// <summary>标记死亡。**由主循环在判定 `!IsAlive` 后调用一次**（幂等）</summary>
        public void MarkDead(long tick)
        {
            if (DeadTick != 0) return;
            DeadTick = tick;
            // 死亡立即关闭突进/跳跃窗口并重置控制（直接写字段，不经 ApplyControl——避免二次 bump）
            ControlSpeedScale = 1f;
            ControlActionLock = false;
            ControlVersion++;
            Displacement = null;
            Movement.Reset();
            // Plan 04：死亡立即清状态（控制派生已重置，SyncControl 不会二次写入）。
            Statuses?.ClearAll((uint)tick);
            BumpCombatVersion();
        }

        /// <summary>
        /// 到点则复活：满血 + 清死亡标记，返回 true。
        /// 调用方（主循环 tick）负责在返回 true 时给本人发 `RespawnNotice`。
        /// 调用方负责更新支持权威复活的玩家的移动基准和落点。
        /// </summary>
        public bool TryRespawn(long nowTick, uint tickMs)
        {
            if (DeadTick == 0) return false;                 // 从未死过 / 已复活
            if (IsAlive) { DeadTick = 0; return false; }     // 兜底：状态不一致时不复活

            long elapsedMs = (nowTick - DeadTick) * tickMs;
            if (elapsedMs < RespawnDelayMs) return false;

            Hp = MaxHp;
            DeadTick = 0;
            LifeId++;
            // 复活同样重置控制与突进/跳跃窗口（直接写字段，不经 ApplyControl——避免二次 bump）
            ControlSpeedScale = 1f;
            ControlActionLock = false;
            ControlVersion++;
            Displacement = null;
            Movement.Reset();
            // Plan 04：新生命周期从干净状态开始（死亡后可能仍有写入口）。
            Statuses?.ClearAll((uint)nowTick);
            BumpCombatVersion();
            return true;
        }

        /// <summary>复活延迟（人类裁定 8 秒；D-2）。**一个常量，演示时可调**</summary>
        public const long RespawnDelayMs = 8_000;

        /// <summary>记录技能最后施放 tick（冷却判定；主循环独占访问）</summary>
        public void RecordCast(int skillId, uint tick) => _lastCastTicks[skillId] = tick;

        /// <summary>冷却就绪判定（tick 差 ≥ 冷却 tick 数；无记录 = 就绪。uint wrap 安全：15Hz 下 ~9.1 年）</summary>
        public bool IsCastReady(int skillId, uint tick, uint cooldownTicks)
            => !_lastCastTicks.TryGetValue(skillId, out var last) || tick - last >= cooldownTicks;

        /// <summary>实体种类（EntityKind：0=Player 默认，1=Npc）——P5 怪物复用实体存储/AOI/快照链</summary>
        public byte Kind { get; internal set; }

        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）</summary>
        public int TemplateId { get; internal set; }

        /// <summary>NPC 身体半径（米；玩家恒 0）。命中判定按表面距离使用。</summary>
        public float BodyRadius { get; internal set; }

        /// <summary>动作/战斗状态（Npc=NpcAnimationState；玩家恒 0——D-P5-5 只做怪物侧）</summary>
        public byte ActionState { get; internal set; }

        /// <summary>脏标记：位置等同步字段在最近一次快照后发生变化（主循环独占访问）</summary>
        public bool IsDirty { get; internal set; }

        public bool IsLoggedIn { get; internal set; }
        public DateTime LastActiveTime { get; internal set; }

        // 属性剑气 v1：权威武器槽、生命周期代次与请求幂等缓存
        public long EquippedWeaponInstanceId { get; internal set; }
        public int EquippedWeaponTemplateId { get; internal set; }
        public uint EquipmentRevision { get; internal set; }
        public uint LifeId { get; internal set; } = 1;

        /// <summary>战斗投影版本戳（单调；任务 8A 接入递增，Health/Combat 位置位时随快照下发）。</summary>
        public uint CombatVersion { get; internal set; }

        /// <summary>战斗投影自上次 CombatStateUpdate 后是否变化（主循环独占访问）。</summary>
        public bool CombatDirty { get; internal set; }

        /// <summary>当前活动施法投影（无 = null；主循环独占访问）。</summary>
        public ActiveCastProjection? ActiveCast { get; internal set; }

        /// <summary>推进版本并标脏，供周期 CombatStateUpdate 发送。</summary>
        public void BumpCombatVersion()
        {
            CombatVersion++;
            CombatDirty = true;
        }

        /// <summary>权威状态运行时包装（Plan 04）；未配置时 null（bot/未接入实体）。</summary>
        public ServerStatusManager? Statuses { get; private set; }

        /// <summary>延迟初始化：登录 / 怪物生成 / 首次命中时配置，避免改所有构造函数。
        /// controlSink = 宿主唯一控制入口（玩家传 Server3C.ApplyPlayerControl；null 直写）。</summary>
        public void ConfigureStatuses(ElementalCatalogDefinition catalog,
            System.Action<float, bool>? controlSink = null)
            => Statuses ??= new ServerStatusManager(this, catalog, controlSink);

        /// <summary>事件序号（按目标单调；Plan 04 填充 HitEntry.EventSeq）。</summary>
        public uint NextEventSeq { get; private set; }

        public uint AllocateEventSeq() => ++NextEventSeq;

        /// <summary>该会话是否启用接收 tick 位移预算（登录能力位 PvpMovementBudgetV1）。</summary>
        public bool StrictMovementBudget { get; internal set; }

        /// <summary>权威控制状态（Plan 03 由 ApplyPlayerControl 注入；Plan 04 由状态运行时写）。</summary>
        public float ControlSpeedScale { get; internal set; } = 1f;
        public bool ControlActionLock { get; internal set; }
        public uint ControlVersion { get; internal set; }

        public MovementBudgetState Movement { get; } = new MovementBudgetState();

        /// <summary>当前权威突进窗口；null = 无。</summary>
        public DisplacementWindow? Displacement { get; internal set; }

        public void ApplyControl(float speedScale, bool actionLock)
        {
            if (!float.IsFinite(speedScale)) speedScale = 0f;
            speedScale = Math.Clamp(speedScale, 0f, 4f);
            if (speedScale == ControlSpeedScale && actionLock == ControlActionLock) return;
            ControlSpeedScale = speedScale;
            ControlActionLock = actionLock;
            ControlVersion++;
            BumpCombatVersion();
        }

        /// <summary>登录基线用：设置上限并回满（只在会话初始化调用；中途改上限不heal）。</summary>
        public void SetMaxHp(float maxHp)
        {
            if (!float.IsFinite(maxHp) || maxHp <= 0f) return;
            MaxHp = maxHp;
            Hp = maxHp;
            BumpCombatVersion();
        }

        /// <summary>会话内武器实例 → 模板。</summary>
        public readonly Dictionary<long, int> WeaponInstanceTemplates = new Dictionary<long, int>();

        /// <summary>该次普攻后最早可再次出手的 tick（按施法时攻速快照固定，换装不提前解锁）。</summary>
        public uint NextAllowedAttackTick { get; internal set; }

        internal uint LastEquipSeq { get; set; }
        internal readonly Dictionary<uint, EquipWeaponConfirm> RecentEquipConfirms =
            new Dictionary<uint, EquipWeaponConfirm>();

        internal uint LastCastSeq { get; set; }
        internal readonly Dictionary<uint, CastConfirm> RecentCastConfirms =
            new Dictionary<uint, CastConfirm>();

        public PlayerState(long playerId)
        {
            PlayerId = playerId;
            Position = Vector3.zero;
            LastActiveTime = DateTime.UtcNow;
        }

        public void UpdatePosition(Vector3 position, float rotation, float speed, long timestamp, uint sequence)
        {
            Position = position;
            Rotation = rotation;
            Speed = speed;
            LastUpdateTimestamp = timestamp;
            LastReceivedSequence = sequence;
            LastActiveTime = DateTime.UtcNow;
            IsDirty = true;
        }
    }
}

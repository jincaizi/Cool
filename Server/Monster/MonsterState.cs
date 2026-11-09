using KcpServer.Config;

namespace KcpServer.Monster
{
    public enum MonsterMode
    {
        Patrol = 0,
        Combat = 1,
        Evade = 2,
        Dead = 3
    }

    public enum MonsterDamageKind
    {
        Direct = 0,
        Dot = 1
    }

    /// <summary>
    /// 怪物运行时 AI 状态（主循环线程独占，R2）——与 EntityStore.PlayerState 配对：
    /// 位置/HP/冷却权威在 PlayerState；AI 决策状态（模式/巡逻/仇恨/生死计时）在此。
    /// P2：模式 = Patrol | Combat | Evade | Dead（替换旧 AlertLevel）。
    /// </summary>
    public sealed class MonsterState
    {
        public MonsterActionRunner? ActionRunner { get; set; }
        public long NpcId { get; }
        public int TemplateId { get; }
        public MonsterData Config { get; }

        public MonsterMode Mode { get; set; } = MonsterMode.Patrol;
        public ThreatTable Threats { get; } = new ThreatTable();

        public long? TargetId { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public Vector3 PatrolCenter { get; set; }
        public Vector3[] PatrolPoints { get; set; } = System.Array.Empty<Vector3>();
        public int CurrentPatrolIndex { get; set; }
        public Vector3? LastKnownTargetPosition { get; set; }

        // 目标丢失/脱战计时（P2 模式机）
        public uint? NoValidTargetSinceTick { get; set; }
        public uint? EvadeStartTick { get; set; }
        public uint PostEvadeGraceUntilTick { get; set; }
        public long? TauntTargetId { get; set; }
        public uint TauntUntilTick { get; set; }
        public bool NeedsReposition { get; set; }
        public int DesiredSkillId;
        public uint? SetupStartTick;
        public uint SetupRetryTick;
        public uint? SearchStartTick;
        public int ObservedQueries, UnreachableCount;
        public readonly System.Collections.Generic.Dictionary<long,uint> UnreachableUntil = new();

        public bool IsTauntActive(uint tick)
            => TauntTargetId.HasValue && tick < TauntUntilTick;

        /// <summary>配置里第一条技能（测试/兼容用；生产选择走 SkillSelector）。</summary>
        public int PrimarySkillId
            => Config.skills.Count > 0 ? Config.skills[0].skillId : 0;

        /// <summary>路点跟随器（恒非 null；无导航时内部回退直线 MoveTo）</summary>
        public PathFollower? Follower { get; set; }

        // tick 内暂存位置/朝向（BT/移动系统写，模块 tick 末尾 flush 到 PlayerState）
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Moving; // 本 tick 是否在移动（快照 Speed 取值）

        /// <summary>上一 tick 的实际位移（米/tick）——ORCA 当"当前速度"用（半平面的锚点）。
        /// 单位是**每 tick 位移**不是 m/s：解算器与各移动系统统一在这个单位里工作，
        /// 好处是无邻居时解出的期望位移可以原样积分（逐位不变）。</summary>
        public Vector3 CurrentVelocity;

        /// <summary>前摇到期 tick（决策 tick + CombatRules.HitDelayTicks）——HasValue 表示
        /// **前摇中**：站定、朝目标转向、锁住行为树（D-10 前摇 = 承诺）。由模块在到期 tick 自清理。</summary>
        public uint? AttackResolveTick { get; set; }

        // 受击硬直窗口（玩家命中 → Hit 态 HitDisplayTicks；窗口内跳过行为树）
        public uint? HitStartTick;

        // 死亡展示窗口（Death 态停驻 DeathDisplayTicks 后移除实体 + 调度重生）
        public uint? DeathTick;

        // 上次观测 HP（裁决在怪物 tick 之后发生——tick+1 察觉玩家命中/死亡转移）
        public float LastHp;

        /// <summary>行为树根（Spawn 时装配）</summary>
        public BtNode? BehaviorTree { get; set; }

        public MonsterState(long npcId, int templateId, MonsterData config)
        {
            NpcId = npcId;
            TemplateId = templateId;
            Config = config;
        }
    }
}

using KcpServer.Config;

namespace KcpServer.Monster
{
    /// <summary>
    /// P5 怪物运行时 AI 状态（主循环线程独占，R2）——与 EntityStore.PlayerState 配对：
    /// 位置/HP/冷却权威在 PlayerState；AI 决策状态（黑板/巡逻/仇恨/生死计时）在此。
    /// 移植自旧 Server/AI（AiBlackboard + AiComponent 合并；删线程/网络/技能层——
    /// 攻击冷却走 PlayerState.IsCastReady，技能数据走 CombatRules）。
    /// </summary>
    public sealed class MonsterState
    {
        public long NpcId { get; }
        public int TemplateId { get; }
        public MonsterData Config { get; }

        // 黑板（旧 AiBlackboard 语义）
        public AlertLevel AlertLevel { get; set; } = AlertLevel.PEACE;
        public long? TargetId { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public Vector3 PatrolCenter { get; set; }
        public Vector3[] PatrolPoints { get; set; } = System.Array.Empty<Vector3>();
        public int CurrentPatrolIndex { get; set; }
        public Vector3? LastKnownTargetPosition { get; set; }

        // tick 内暂存位置/朝向（BT/移动系统写，模块 tick 末尾 flush 到 PlayerState）
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Moving; // 本 tick 是否在移动（快照 Speed 取值）

        /// <summary>上一 tick 的实际位移（米/tick）——ORCA 当"当前速度"用（半平面的锚点）。
        /// 单位是**每 tick 位移**不是 m/s：解算器与各移动系统统一在这个单位里工作，
        /// 好处是无邻居时解出的期望位移可以原样积分（逐位不变）。</summary>
        public Vector3 CurrentVelocity;

        // 攻击技能 id（模板映射：1→50001，2→50002；D-P5-2）
        public int AttackSkillId { get; }

        // 受击硬直窗口（玩家命中 → Hit 态 HitDisplayTicks；窗口内跳过行为树）
        public uint? HitStartTick;

        // 死亡展示窗口（Death 态停驻 DeathDisplayTicks 后移除实体 + 调度重生）
        public uint? DeathTick;

        // 上次观测 HP（裁决在怪物 tick 之后发生——tick+1 察觉玩家命中/死亡转移）
        public float LastHp;

        /// <summary>行为树根（Spawn 时装配）</summary>
        public BtNode? BehaviorTree { get; set; }

        public MonsterState(long npcId, int templateId, MonsterData config, int attackSkillId)
        {
            NpcId = npcId;
            TemplateId = templateId;
            Config = config;
            AttackSkillId = attackSkillId;
        }
    }

    /// <summary>警报等级（旧 Server/AI 移植；HOSTILE 只在获取目标时置位，Return 完成回 PEACE）</summary>
    public enum AlertLevel
    {
        PEACE = 0,
        HOSTILE = 1
    }
}

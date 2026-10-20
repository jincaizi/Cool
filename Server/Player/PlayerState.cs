using System;

namespace KcpServer
{
    /// <summary>
    /// 玩家权威状态
    /// </summary>
    public sealed class PlayerState
    {
        public long PlayerId { get; }
        public string PlayerName { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public Vector3 Position { get; internal set; }
        public float Rotation { get; internal set; }
        public float Speed { get; internal set; }
        public long LastUpdateTimestamp { get; internal set; }
        public uint LastReceivedSequence { get; internal set; }

        /// <summary>P4 战斗状态（服务器权威；登录重建 PlayerState 即满血——复活另见 D-2 的 8 秒计时）</summary>
        public float Hp { get; internal set; } = 100f;
        public float MaxHp { get; internal set; } = 100f;
        public bool IsAlive => Hp > 0f;
        private readonly System.Collections.Generic.Dictionary<int, uint> _lastCastTicks =
            new System.Collections.Generic.Dictionary<int, uint>();

        /// <summary>服务器权威扣血（命中裁决调用；可到负值——死亡判定唯一依据 IsAlive，复活见 D-2）</summary>
        public void ApplyDamage(float damage) => Hp -= damage;

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
        }

        /// <summary>
        /// 到点则复活：满血 + 清死亡标记，返回 true。
        /// 调用方（主循环 tick）负责在返回 true 时给本人发 `RespawnNotice`。
        /// **位置不在这里改**——玩家位置由客户端上报（D-N5），服务端摆位置会被下一次上报覆盖。
        /// </summary>
        public bool TryRespawn(long nowTick, uint tickMs)
        {
            if (DeadTick == 0) return false;                 // 从未死过 / 已复活
            if (IsAlive) { DeadTick = 0; return false; }     // 兜底：状态不一致时不复活

            long elapsedMs = (nowTick - DeadTick) * tickMs;
            if (elapsedMs < RespawnDelayMs) return false;

            Hp = MaxHp;
            DeadTick = 0;
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

        /// <summary>动作/战斗状态（Npc=NpcAnimationState；玩家恒 0——D-P5-5 只做怪物侧）</summary>
        public byte ActionState { get; internal set; }

        /// <summary>脏标记：位置等同步字段在最近一次快照后发生变化（主循环独占访问）</summary>
        public bool IsDirty { get; internal set; }

        public bool IsLoggedIn { get; internal set; }
        public DateTime LastActiveTime { get; internal set; }

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

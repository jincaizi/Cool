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

        /// <summary>P4 战斗状态（服务器权威；登录重建 PlayerState 即满血——无复活，见 D-P4-8）</summary>
        public float Hp { get; internal set; } = 100f;
        public float MaxHp { get; internal set; } = 100f;
        public bool IsAlive => Hp > 0f;
        private readonly System.Collections.Generic.Dictionary<int, uint> _lastCastTicks =
            new System.Collections.Generic.Dictionary<int, uint>();

        /// <summary>服务器权威扣血（命中裁决调用；可到负值——死亡判定唯一依据 IsAlive，无复活 D-P4-8）</summary>
        public void ApplyDamage(float damage) => Hp -= damage;

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

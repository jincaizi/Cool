using System;
using System.Collections.Generic;

namespace KcpServer.Monster
{
    /// <summary>
    /// 顶层模式机（P2）。每 tick 在行为树之前运行：有效性清理 → 目标选择 → 模式转移。
    /// 不移动、不施法；移动/施法仍由 BT 动作负责。
    /// </summary>
    public sealed class MonsterBrain
    {
        public const uint TargetLostGraceTicks = 45;   // 3s
        public const uint PostEvadeGraceTicks = 30;    // 2s
        public const uint ReturnTimeoutTicks = 300;    // 20s

        private readonly EntityStore _store;

        public MonsterBrain(EntityStore store) => _store = store;

        public void Update(MonsterState m, PlayerState entity, uint tick, bool allowEvadeTransition)
        {
            if (m.Mode == MonsterMode.Dead || m.DeathTick.HasValue) return;

            bool currentOutOfRange = CurrentTargetOutOfRange(m);
            PruneInvalidThreat(m);

            if (m.Mode == MonsterMode.Patrol)
            {
                if (tick < m.PostEvadeGraceUntilTick) return;
                if (TryAcquireBySight(m, entity)) m.Mode = MonsterMode.Combat;
                return;
            }

            if (m.Mode == MonsterMode.Evade)
            {
                if (DistanceXZ(m.Position, m.SpawnPosition) <= 0.5f)
                    ArriveHome(m, entity, tick);
                return;
            }

            // Combat
            if (allowEvadeTransition && DistanceXZ(m.Position, m.SpawnPosition) > m.Config.LeashRadiusOf)
            {
                EnterEvade(m, tick);
                return;
            }

            // 前摇锁目标（spec §5.1）：只锁 TargetId，不换目标；LastKnownTargetPosition 仍按
            // spec §5.2 每 tick 从目标实体更新（怪物在前摇期会以 90°/s 朝目标转向）。
            long? selected = m.AttackResolveTick.HasValue
                ? m.TargetId
                : TargetSelector.Select(m.Threats, m.TargetId,
                    id => IsValidTarget(m, id),
                    m.IsTauntActive(tick) ? m.TauntTargetId : null);

            if (selected.HasValue)
            {
                var target = _store.GetPlayer(selected.Value);
                if (target == null) { selected = null; }
                else
                {
                    m.TargetId = selected;
                    m.LastKnownTargetPosition = target.Position;
                    m.NoValidTargetSinceTick = null;
                    m.NeedsReposition = SkillSelector.NeedsReposition(
                        m, entity, DistanceXZ(m.Position, target.Position));
                    return;
                }
            }

            m.TargetId = null;
            m.LastKnownTargetPosition = null;
            m.NeedsReposition = false;
            if (!allowEvadeTransition) return;

            if (currentOutOfRange) { EnterEvade(m, tick); return; }
            m.NoValidTargetSinceTick ??= tick;
            if (tick - m.NoValidTargetSinceTick.Value >= TargetLostGraceTicks)
                EnterEvade(m, tick);
        }

        public void OnDamageTaken(MonsterState m, long attackerId, float damage, MonsterDamageKind kind)
        {
            if (m.Mode == MonsterMode.Dead || m.Mode == MonsterMode.Evade || m.DeathTick.HasValue) return;
            var attacker = _store.GetPlayer(attackerId);
            if (attacker == null || attacker.Kind != (byte)EntityKind.Player
                || !attacker.IsAlive || !attacker.IsLoggedIn) return;

            m.Threats.AddDamage(attackerId, damage);
            if (m.Mode == MonsterMode.Patrol) m.Mode = MonsterMode.Combat;
            m.NoValidTargetSinceTick = null;

            if (!m.TargetId.HasValue)
            {
                m.TargetId = attackerId;
                m.LastKnownTargetPosition = attacker.Position;
            }
        }

        public void Taunt(MonsterState m, long playerId, uint durationTicks, uint tick)
        {
            if (m.Mode == MonsterMode.Dead || m.Mode == MonsterMode.Evade || m.DeathTick.HasValue) return;
            var player = _store.GetPlayer(playerId);
            if (player == null || player.Kind != (byte)EntityKind.Player
                || !player.IsAlive || !player.IsLoggedIn) return;

            m.Threats.Ensure(playerId);
            m.TauntTargetId = playerId;
            m.TauntUntilTick = tick + durationTicks;
            if (m.Mode == MonsterMode.Patrol) m.Mode = MonsterMode.Combat;
            m.NoValidTargetSinceTick = null;
            if (!m.AttackResolveTick.HasValue)
            {
                m.TargetId = playerId;
                m.LastKnownTargetPosition = player.Position;
            }
        }

        public void EnterEvade(MonsterState m, uint tick)
        {
            m.Mode = MonsterMode.Evade;
            m.EvadeStartTick = tick;
            m.Threats.Clear();
            m.TargetId = null;
            m.LastKnownTargetPosition = null;
            m.NoValidTargetSinceTick = null;
            m.NeedsReposition = false;
            m.TauntTargetId = null;
            m.TauntUntilTick = 0;
            m.Follower?.Reset();
        }

        public void ArriveHome(MonsterState m, PlayerState entity, uint tick)
        {
            entity.Hp = entity.MaxHp;
            entity.ActionState = (byte)NpcAnimationState.Idle;
            m.LastHp = entity.Hp;
            m.Mode = MonsterMode.Patrol;
            m.EvadeStartTick = null;
            m.Threats.Clear();
            m.TargetId = null;
            m.LastKnownTargetPosition = null;
            m.CurrentPatrolIndex = 0;
            m.PostEvadeGraceUntilTick = tick + PostEvadeGraceTicks;
            m.NoValidTargetSinceTick = null;
            m.NeedsReposition = false;
            m.TauntTargetId = null;
            m.TauntUntilTick = 0;
            m.Follower?.Reset();
        }

        private void PruneInvalidThreat(MonsterState m)
        {
            List<long>? remove = null;
            foreach (long playerId in m.Threats.PlayerIds)
                if (!IsValidTarget(m, playerId)) (remove ??= new List<long>()).Add(playerId);
            if (remove == null) return;
            foreach (long playerId in remove) m.Threats.Remove(playerId);
        }

        private bool IsValidTarget(MonsterState m, long playerId)
        {
            var player = _store.GetPlayer(playerId);
            if (player == null || player.Kind != (byte)EntityKind.Player
                || !player.IsAlive || !player.IsLoggedIn) return false;
            return DistanceXZ(player.Position, m.Position) <= m.Config.DropAggroRadiusOf;
        }

        private bool CurrentTargetOutOfRange(MonsterState m)
        {
            if (!m.TargetId.HasValue) return false;
            var player = _store.GetPlayer(m.TargetId.Value);
            if (player == null || player.Kind != (byte)EntityKind.Player
                || !player.IsAlive || !player.IsLoggedIn) return false;
            return DistanceXZ(player.Position, m.Position) > m.Config.DropAggroRadiusOf;
        }

        private bool TryAcquireBySight(MonsterState m, PlayerState entity)
        {
            long? closestId = null;
            Vector3 closestPosition = default;
            float closestSqr = float.MaxValue;

            foreach (var player in _store.All)
            {
                if (player.Kind != (byte)EntityKind.Player || !player.IsAlive || !player.IsLoggedIn)
                    continue;
                if (!TargetDetector.CanDetect(entity.Rotation, m.Position, player.Position,
                        m.Config.detectionRadius, m.Config.visionAngle)) continue;

                float dx = player.Position.X - m.Position.X;
                float dz = player.Position.Z - m.Position.Z;
                float sqr = dx * dx + dz * dz;
                if (sqr < closestSqr
                    || (sqr == closestSqr && closestId.HasValue && player.PlayerId < closestId.Value))
                {
                    closestSqr = sqr;
                    closestId = player.PlayerId;
                    closestPosition = player.Position;
                }
            }

            if (!closestId.HasValue) return false;
            m.Threats.Clear();
            m.Threats.AddInitial(closestId.Value);
            m.TargetId = closestId;
            m.LastKnownTargetPosition = closestPosition;
            m.NoValidTargetSinceTick = null;
            return true;
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}

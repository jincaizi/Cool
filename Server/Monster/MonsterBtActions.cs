using System;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer.Monster
{
    /// <summary>
    /// 怪物行为动作（旧 BtActions 移植）。
    /// P5 修正：Chase 进入攻击距离即 Success——旧树 Chase 恒 Running 会饿死序列后续
    /// Attack 节点（序列被阻塞，怪物永远不攻击——旧代码死代码期缺陷，见偏差记录）。
    /// P2：攻击距离改由 SkillSelector 的技能区间决定；新增 Reposition / Evade（取代 Return）。
    /// </summary>
    public static class MonsterBtActions
    {
        /// <summary>前摇期转身速度（度/秒）——**比移动态 180 慢**（D-7 人类裁定）：
        /// 把冲刺绕圈的半径门槛从 1.2m 放宽到 1.75m，绕背才是可执行的操作。</summary>
        public const float WindupTurnSpeedDegPerSecond = 90f;

        /// <summary>实际位移小于该值（米）视为未移动（ORCA 清零/无路径/到点）。</summary>
        private const float MovementEpsilon = 0.001f;

        public static BtStatus Patrol(MonsterTickContext ctx)
        {
            var m = ctx.State;
            if (m.PatrolPoints.Length == 0)
            {
                SetMovementState(ctx, moved: false);
                return BtStatus.Success;
            }

            int idx = m.CurrentPatrolIndex % m.PatrolPoints.Length;
            Vector3 target = m.PatrolPoints[idx];

            float moved = m.Follower != null
                ? (m.Follower.Tick(m, target, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
                : ctx.Mover.MoveTo(m, target, ctx.DeltaTime);

            if (ctx.Mover.HasReached(m, target, 0.5f))
            {
                m.CurrentPatrolIndex++;
                SetMovementState(ctx, moved: false);
            }
            else
            {
                SetMovementState(ctx, moved: moved > MovementEpsilon);
            }
            return BtStatus.Running;
        }

        public static BtStatus Chase(MonsterTickContext ctx)
        {
            var m = ctx.State;

            if (m.AttackResolveTick.HasValue) return BtStatus.Success;

            // 目标丢失（死亡/离线/出 drop 半径）后 brain 会清 LastKnown；此时 Chase 必须
            // 显式写 Idle/0 速再返回，否则会保留上一 tick 的 Run/Attack 与 moveSpeed 广播，
            // 一路假跑到进入 Evade（最长 45 tick）。
            if (!m.LastKnownTargetPosition.HasValue)
            {
                SetMovementState(ctx, moved: false);
                return BtStatus.Success;
            }

            Vector3 target = m.LastKnownTargetPosition.Value;
            float targetDistance = DistanceXZ(m.Position, target);
            bool inRange = SkillSelector.IsWithinAnySkillRange(m, ctx.Entity, targetDistance);
            if (inRange && (m.Follower == null
                || m.Follower.HasLineOfSight(m.Position, target, m.Config.bodyRadius)))
                return BtStatus.Success;

            float moved = m.Follower != null
                ? (m.Follower.Tick(m, target, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
                : ctx.Mover.MoveTo(m, target, ctx.DeltaTime);
            SetMovementState(ctx, moved > MovementEpsilon);
            return BtStatus.Running;
        }

        public static BtStatus Attack(MonsterTickContext ctx)
        {
            var m = ctx.State;
            var entity = ctx.Entity;

            if (m.AttackResolveTick.HasValue)
            {
                m.Moving = false;
                if (m.LastKnownTargetPosition.HasValue)
                    ctx.Mover.TurnTowards(m, m.LastKnownTargetPosition.Value, ctx.DeltaTime,
                        WindupTurnSpeedDegPerSecond);
                return BtStatus.Running;
            }

            if (!m.TargetId.HasValue) return BtStatus.Failure;

            Vector3 target = m.LastKnownTargetPosition ?? m.Position;
            float distance = DistanceXZ(m.Position, target);
            if (!SkillSelector.IsWithinAnySkillRange(m, entity, distance)) return BtStatus.Failure;

            if (m.Follower != null && !m.Follower.HasLineOfSight(m.Position, target, m.Config.bodyRadius))
            {
                m.Moving = false;
                entity.ActionState = (byte)NpcAnimationState.Idle;
                return BtStatus.Failure;
            }

            m.Moving = false;

            var selected = SkillSelector.Select(m, entity, distance, ctx.Tick);
            if (selected == null) return BtStatus.Running; // 射程内但冷却：站定等待

            if (!MonsterCastBuilder.TryBuild(m, selected.Value, target, out var request))
                return BtStatus.Failure;

            ctx.Module.QueueCast(m.NpcId, request);
            m.AttackResolveTick = ctx.Tick + CombatRules.HitDelayTicks(selected.Value.Entry);
            entity.ActionState = (byte)NpcAnimationState.Attack;
            return BtStatus.Running;
        }

        public static BtStatus Reposition(MonsterTickContext ctx)
        {
            var m = ctx.State;
            if (!m.LastKnownTargetPosition.HasValue) return BtStatus.Failure;

            Vector3 target = m.LastKnownTargetPosition.Value;
            float distance = DistanceXZ(m.Position, target);
            float desired = SkillSelector.RepositionDistance(m, ctx.Entity, distance);
            if (desired <= 0f || distance >= desired)
            {
                SetMovementState(ctx, moved: false);
                return BtStatus.Success;
            }

            Vector3 away = m.Position - target;
            away.Y = 0f;
            if (away.X * away.X + away.Z * away.Z < 0.0001f) away = new Vector3(0f, 0f, -1f);
            away = away.Normalize();
            Vector3 goal = target + away * desired;

            float moved = m.Follower != null
                ? (m.Follower.Tick(m, goal, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
                : ctx.Mover.MoveTo(m, goal, ctx.DeltaTime);
            SetMovementState(ctx, moved > MovementEpsilon);
            return BtStatus.Running;
        }

        public static BtStatus Evade(MonsterTickContext ctx)
        {
            var m = ctx.State;
            float moved = m.Follower != null
                ? (m.Follower.Tick(m, m.SpawnPosition, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
                : ctx.Mover.MoveTo(m, m.SpawnPosition, ctx.DeltaTime);

            if (ctx.Mover.HasReached(m, m.SpawnPosition, 0.5f))
            {
                SetMovementState(ctx, moved: false);
                return BtStatus.Success;
            }
            SetMovementState(ctx, moved: moved > MovementEpsilon);
            return BtStatus.Running;
        }

        /// <summary>按**实际位移**写 Moving/ActionState（spec §4.4）。查询失败/预算拒绝/ORCA 清零时
        /// 不能再写 Run——否则客户端播跑步动画而速度是 0。</summary>
        private static void SetMovementState(MonsterTickContext ctx, bool moved)
        {
            ctx.State.Moving = moved;
            ctx.Entity.ActionState = (byte)(moved ? NpcAnimationState.Run : NpcAnimationState.Idle);
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}

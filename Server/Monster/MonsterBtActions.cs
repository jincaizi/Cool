using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer.Monster
{
    /// <summary>
    /// 怪物行为动作（旧 BtActions 移植）。
    /// P5 修正：Chase 进入攻击距离即 Success——旧树 Chase 恒 Running 会饿死序列后续
    /// Attack 节点（序列被阻塞，怪物永远不攻击——旧代码死代码期缺陷，见偏差记录）。
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

            // 前摇 = 承诺（D-10/F12）：前摇期把本 tick 让给 Attack 的前摇分支——
            // 不移动、不写 ActionState（写 Run 会当场截断挥砍），也不因距离变化抢先追击。
            // 这一条和 Hostile 条件放宽（MonsterModule）是一对：前者管"进得了 Attack"，后者管"不中途改走"。
            if (m.AttackResolveTick.HasValue) return BtStatus.Success;

            Vector3 target = m.LastKnownTargetPosition ?? m.Position;
            // 停步判定用 navmesh 投影点；实际命中裁决仍用玩家真实位置。
            // 距离内但隔墙（无 LOS）不能停步：继续 Follower.Tick 绕行，否则会冻结在墙角
            // 且上一 tick 的 Run/速度继续广播（客户端“跑动但不动”）。
            Vector3 stopTarget = target;
            if (m.Follower != null) m.Follower.TryProject(target, out stopTarget);
            bool inRange = ctx.Mover.HasReached(m, stopTarget, m.Config.attackRange);
            if (inRange && (m.Follower == null
                || m.Follower.HasLineOfSight(m.Position, target, m.Config.bodyRadius)))
                return BtStatus.Success;

            float moved = m.Follower != null
                ? (m.Follower.Tick(m, target, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
                : ctx.Mover.MoveTo(m, target, ctx.DeltaTime);
            SetMovementState(ctx, moved: moved > MovementEpsilon);
            return BtStatus.Running;
        }

        public static BtStatus Attack(MonsterTickContext ctx)
        {
            var m = ctx.State;
            var entity = ctx.Entity;

            // 前摇 = 承诺（D-10）：站定 + 朝目标转向，不受距离/目标丢失影响；
            // 只有死亡能取消（死亡后实体被移除/IsAlive=false，判定阶段自然丢弃这一刀）。
            if (m.AttackResolveTick.HasValue)
            {
                m.Moving = false;
                if (m.LastKnownTargetPosition.HasValue)
                    ctx.Mover.TurnTowards(m, m.LastKnownTargetPosition.Value, ctx.DeltaTime, WindupTurnSpeedDegPerSecond);
                return BtStatus.Running;
            }

            // 目标丢失（死亡/离场）→ 失败回 Chase/Return
            if (!m.TargetId.HasValue) return BtStatus.Failure;

            // 目标移出攻击距离 → 失败 → 序列重头 Chase
            Vector3 target = m.LastKnownTargetPosition ?? m.Position;
            float dx = m.Position.X - target.X, dz = m.Position.Z - target.Z;
            if (dx * dx + dz * dz > m.Config.attackRange * m.Config.attackRange) return BtStatus.Failure;

            if (m.Follower != null && !m.Follower.HasLineOfSight(m.Position, target, m.Config.bodyRadius))
            {
                // 隔墙不能出手；同时落回 Idle/零速，避免沿用上一 tick 的 Run 广播
                m.Moving = false;
                ctx.Entity.ActionState = (byte)NpcAnimationState.Idle;
                return BtStatus.Failure;
            }

            m.Moving = false;

            // 冷却门（tick 基准——与 P4 玩家同机制；冷却在**决策**时记录，D-3）
            // 表缺失校验已前移 SpawnMonster（fail fast——避免在攻击距离静默冻结）；此处为防御分支，正常不可达
            if (!CombatRules.TryGet(m.AttackSkillId, out var entry)) return BtStatus.Failure;
            if (!entity.IsCastReady(m.AttackSkillId, ctx.Tick, CombatRules.CooldownTicks(entry)))
                return BtStatus.Running;

            ctx.Module.QueueCast(m.NpcId, new CastRequest
            {
                SkillId = m.AttackSkillId,
                TargetPlayerId = m.TargetId.Value, // 显式仇恨目标（HitResolver 按 id + 距离 + 锥角裁决，D-P5-4/D-6）
                Shape = new HitShape
                {
                    ShapeType = 0, // Single——范围 = 配置 attackRange（须 ≤ 表上限——ValidateShape 校验）
                    Range = m.Config.attackRange,
                    Angle = 120f
                }
            });
            m.AttackResolveTick = ctx.Tick + CombatRules.HitDelayTicks(entry); // 前摇到期（判定帧）
            entity.ActionState = (byte)NpcAnimationState.Attack;
            return BtStatus.Running; // 保持攻击态；下一 tick 前摇分支再判
        }

        public static BtStatus Return(MonsterTickContext ctx)
        {
            var m = ctx.State;
            float moved = m.Follower != null
                ? (m.Follower.Tick(m, m.SpawnPosition, ctx.Mover, ctx.DeltaTime, ctx.Tick) ? 1f : 0f)
                : ctx.Mover.MoveTo(m, m.SpawnPosition, ctx.DeltaTime);

            if (ctx.Mover.HasReached(m, m.SpawnPosition, 0.5f))
            {
                m.AlertLevel = AlertLevel.PEACE;
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
    }
}

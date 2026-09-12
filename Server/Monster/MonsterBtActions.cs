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
        public static BtStatus Patrol(MonsterTickContext ctx)
        {
            var m = ctx.State;
            if (m.PatrolPoints.Length == 0) return BtStatus.Success;

            int idx = m.CurrentPatrolIndex % m.PatrolPoints.Length;
            Vector3 target = m.PatrolPoints[idx];

            ctx.Mover.MoveTo(m, target, ctx.DeltaTime);
            if (ctx.Mover.HasReached(m, target))
            {
                m.CurrentPatrolIndex++;
                m.Moving = false;
                ctx.Entity.ActionState = (byte)NpcAnimationState.Idle;
            }
            else
            {
                m.Moving = true;
                ctx.Entity.ActionState = (byte)NpcAnimationState.Run;
            }
            return BtStatus.Running;
        }

        public static BtStatus Chase(MonsterTickContext ctx)
        {
            var m = ctx.State;
            Vector3 target = m.LastKnownTargetPosition ?? m.Position;

            // 进入攻击距离即成功（P5 修正）——让序列前进到 Attack
            if (ctx.Mover.HasReached(m, target, m.Config.attackRange)) return BtStatus.Success;

            ctx.Mover.MoveTo(m, target, ctx.DeltaTime);
            m.Moving = true;
            ctx.Entity.ActionState = (byte)NpcAnimationState.Run;
            return BtStatus.Running;
        }

        public static BtStatus Attack(MonsterTickContext ctx)
        {
            var m = ctx.State;
            var entity = ctx.Entity;

            // 目标丢失（死亡/离场）→ 失败回 Chase/Return
            if (!m.TargetId.HasValue) return BtStatus.Failure;

            // 目标移出攻击距离 → 失败 → 序列重头 Chase
            Vector3 target = m.LastKnownTargetPosition ?? m.Position;
            float dx = m.Position.X - target.X, dz = m.Position.Z - target.Z;
            if (dx * dx + dz * dz > m.Config.attackRange * m.Config.attackRange) return BtStatus.Failure;

            m.Moving = false;

            // 冷却门（tick 基准——与 P4 玩家同机制；裁决在怪物 tick 后发生，记录即时生效）
            // 表缺失校验已前移 SpawnMonster（fail fast——避免在攻击距离静默冻结）；此处为防御分支，正常不可达
            if (!CombatRules.TryGet(m.AttackSkillId, out var entry)) return BtStatus.Failure;
            if (!entity.IsCastReady(m.AttackSkillId, ctx.Tick, CombatRules.CooldownTicks(entry)))
                return BtStatus.Running;

            ctx.Module.QueueCast(m.NpcId, new CastRequest
            {
                SkillId = m.AttackSkillId,
                TargetPlayerId = m.TargetId.Value, // 显式仇恨目标（HitResolver 按 id + 距离裁决，D-P5-4）
                Shape = new HitShape
                {
                    ShapeType = 0, // Single——范围 = 配置 attackRange（须 ≤ 表上限——ValidateShape 校验）
                    Range = m.Config.attackRange,
                    Angle = 120f
                }
            });
            entity.ActionState = (byte)NpcAnimationState.Attack;
            return BtStatus.Running; // 保持攻击态；下一 tick 冷却门再判
        }

        public static BtStatus Return(MonsterTickContext ctx)
        {
            var m = ctx.State;
            ctx.Mover.MoveTo(m, m.SpawnPosition, ctx.DeltaTime);
            if (ctx.Mover.HasReached(m, m.SpawnPosition))
            {
                m.AlertLevel = AlertLevel.PEACE;
                m.Moving = false;
                ctx.Entity.ActionState = (byte)NpcAnimationState.Idle;
                return BtStatus.Success;
            }
            m.Moving = true;
            ctx.Entity.ActionState = (byte)NpcAnimationState.Run;
            return BtStatus.Running;
        }
    }
}

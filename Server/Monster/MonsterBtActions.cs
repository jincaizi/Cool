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
                || m.Follower.HasAttackLineOfSight(m.Position, target)))
                return BtStatus.Success;

            float moved;
            if(m.ActionRunner != null && m.Follower != null) {
                // B4：停滞豁免用当前血量条件下的最大技能射程（含 50005 的 6m），而不是模板 attackRange。
                var result=m.Follower.TickResult(m,target,ctx.Mover,ctx.DeltaTime,ctx.Tick,
                    SkillSelector.MaxRangeFor(m,ctx.Entity));
                moved=result.Status==MovementStatus.Moving?1f:0f;
                if(result.ValidQueries!=m.ObservedQueries) {
                    m.ObservedQueries=result.ValidQueries;
                    if(result.Status==MovementStatus.Unreachable) m.UnreachableCount++; else if(result.Status!=MovementStatus.PendingBudget) m.UnreachableCount=0;
                }
                if(m.UnreachableCount>=3 && m.TargetId.HasValue) {
                    m.UnreachableUntil[m.TargetId.Value]=ctx.Tick+75;
                    m.SearchStartTick ??= ctx.Tick; m.TargetId=null;m.UnreachableCount=0;
                }
            } else moved=m.Follower!=null?(m.Follower.Tick(m,target,ctx.Mover,ctx.DeltaTime,ctx.Tick,
                SkillSelector.MaxRangeFor(m,ctx.Entity))?1f:0f):ctx.Mover.MoveTo(m,target,ctx.DeltaTime);
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

            // B4：合法就绪且在射程内的技能优先。旧顺序先跑 TrySetupSkill（SelectPotential
            // 不看距离），半血 DogKnight 会在玩家贴身时反复后撤准备 50005，跳过可立即施放的 50003。
            var selected = SkillSelector.Select(m, entity, distance, ctx.Tick);
            if (selected == null && !SkillSelector.IsWithinAnySkillRange(m, entity, distance))
                return BtStatus.Failure;

            // LOS 先于选招/setup：隔墙时（含技能冷却）落回 Chase 绕行，不能一边隔墙一边后撤准备。
            if (m.Follower != null && !m.Follower.HasAttackLineOfSight(m.Position, target))
            {
                m.Moving = false;
                entity.ActionState = (byte)NpcAnimationState.Idle;
                return BtStatus.Failure;
            }

            if (selected == null)
            {
                // 没有可施放技能时才评估“太近 → 后撤到 minRange”；失败/无进展由 setup 超时与
                // SetupRetryTick 退出，下一次重评近战或追击。
                if (m.ActionRunner != null && TrySetupSkill(ctx)) return BtStatus.Running;

                m.Moving = false;
                return BtStatus.Running; // 射程内但冷却：站定等待
            }

            m.Moving = false;

            if (!MonsterCastBuilder.TryBuild(m, selected.Value, target, out var request))
                return BtStatus.Failure;

            ctx.Module.QueueCast(m.NpcId, request);
            if (m.ActionRunner == null) {
                m.AttackResolveTick = ctx.Tick + CombatRules.HitDelayTicks(selected.Value.Entry);
                entity.ActionState = (byte)NpcAnimationState.Attack;
            }
            return BtStatus.Running;
        }

        private static bool TrySetupSkill(MonsterTickContext ctx)
        {
            var m=ctx.State;
            if(!MonsterActionRunner.Due(ctx.Tick,m.SetupRetryTick) || !m.LastKnownTargetPosition.HasValue) return false;
            var potential=SkillSelector.SelectPotential(m,ctx.Entity,ctx.Tick);
            if(potential==null) return false;
            var skill=potential.Value;
            var target=m.LastKnownTargetPosition.Value;
            float distance=DistanceXZ(m.Position,target);
            if(distance>=skill.Config.minRange) {m.SetupStartTick=null;m.DesiredSkillId=0;return false;}
            m.SetupStartTick ??= ctx.Tick; m.DesiredSkillId=skill.Config.skillId;
            if(unchecked(ctx.Tick-m.SetupStartTick.Value)>=30) { EndSetup(m,ctx.Tick); return false; }
            var away=m.Position-target;away.Y=0;
            if(away.X*away.X+away.Z*away.Z<0.0001f) away=new Vector3(0,0,-1);
            var goal=target+away.Normalize()*(skill.Config.minRange+0.25f);
            if(DistanceXZ(goal,m.SpawnPosition)>m.Config.LeashRadiusOf) {EndSetup(m,ctx.Tick);return false;}
            var result=m.Follower!.TickResult(m,goal,ctx.Mover,ctx.DeltaTime,ctx.Tick);
            if(result.Status==MovementStatus.Unreachable) {EndSetup(m,ctx.Tick);return false;}
            SetMovementState(ctx,result.Status==MovementStatus.Moving);return true;
        }
        private static void EndSetup(MonsterState state,uint tick) { state.SetupStartTick=null;state.DesiredSkillId=0;state.SetupRetryTick=tick+30;state.Follower?.Reset(); }

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
            if (ctx.State.ActionRunner?.CanStartSkill == true) ctx.State.ActionRunner.SetContinuous(moved, ctx.Tick);
            ctx.Entity.ActionState = (byte)(moved ? NpcAnimationState.Run : NpcAnimationState.Idle);
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}

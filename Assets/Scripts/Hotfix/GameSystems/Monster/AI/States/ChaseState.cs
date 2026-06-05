using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    public class ChaseState : AIStateBase
    {
        // Tracks how long the monster has been chasing.
        // Used by evaluate logic to trigger defense after prolonged chase.
        // Reset to 0 on entering Chase; incremented in OnUpdate.
        public float ChaseTimer { get; set; }

        public override MonsterAIState StateType => MonsterAIState.Chase;

        public override void OnEnter(AIContext ctx)
        {
            ChaseTimer = 0f;
            ctx.Movement.Resume();
            ctx.Animator.SetFloat(MonsterAnimHashes.Speed, ctx.Config.ChaseAnimIsRun ? 2f : 1f);
        }

        public override void OnUpdate(AIContext ctx)
        {
            ChaseTimer += ctx.DeltaTime;

            if (ctx.Target != null)
            {
                ctx.Movement.Chase(ctx.Target);
                ctx.Movement.LookAt(ctx.Target.position);
            }
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            // Target lost — return to spawn via idle
            if (ctx.Target == null)
                return MonsterAIState.Idle;

            float dist = Vector3.Distance(ctx.Self.position, ctx.Target.position);

            // Out of chase range — give up
            if (dist > ctx.Config.LeaveRange)
                return MonsterAIState.Idle;

            // In attack range and cooldown ready — attack
            if (dist < ctx.Config.AttackRange && ctx.AttackCooldown <= 0)
                return MonsterAIState.Attack;

            // Check defend trigger conditions (HP low OR chasing too long)
            if (ctx.Config.EnableDefend)
            {
                float hpRatio = ctx.Stats.HP / ctx.Stats.MaxHP;
                if (hpRatio < ctx.Config.DefendHPThreshold
                    || ChaseTimer > ctx.Config.DefendChaseTimeThreshold)
                    return MonsterAIState.Defend;
            }

            return null;
        }
    }
}

using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // DefendState merges the old DefendBehaviour logic into the state class.
    // Block counting is tracked in ctx.BlockCount (incremented by DefendModifier).
    // When block count reaches threshold, counter-attack on exit.
    public class DefendState : AIStateBase
    {
        public override MonsterAIState StateType => MonsterAIState.Defend;

        public override void OnEnter(AIContext ctx)
        {
            ctx.BlockCount = 0;
            ctx.StateTimer = 0f;
            ctx.Movement.Stop();
            ctx.Animator.SetBool(MonsterAnimHashes.IsDefending, true);
        }

        public override void OnUpdate(AIContext ctx)
        {
            ctx.StateTimer += ctx.DeltaTime;
            if (ctx.Target != null)
                ctx.Movement.LookAt(ctx.Target.position);
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            if (ctx.StateTimer < ctx.Config.DefendDuration)
                return null;

            // Defend timer expired. Counter-attack if enough blocks, otherwise chase.
            if (ctx.BlockCount >= ctx.Config.DefendBlockCountToCounter)
                return MonsterAIState.Attack;

            float distToTarget = ctx.Target != null
                ? Vector3.Distance(ctx.Self.position, ctx.Target.position)
                : float.MaxValue;

            if (distToTarget < ctx.Config.AttackRange && ctx.AttackCooldown <= 0)
                return MonsterAIState.Attack;

            return MonsterAIState.Chase;
        }

        public override void OnExit(AIContext ctx)
        {
            ctx.Animator.SetBool(MonsterAnimHashes.IsDefending, false);
            ctx.Movement.Resume();
        }
    }
}

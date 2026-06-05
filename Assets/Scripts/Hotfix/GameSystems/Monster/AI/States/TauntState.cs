using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // Plays a taunt animation after a missed attack, then re-engages.
    // Merges the old TauntBehaviour logic into the state class.
    public class TauntState : AIStateBase
    {
        public override MonsterAIState StateType => MonsterAIState.Taunt;

        public override void OnEnter(AIContext ctx)
        {
            ctx.StateTimer = 0f;
            ctx.Movement.Stop();
            ctx.Animator.SetTrigger(MonsterAnimHashes.Taunt);
        }

        public override void OnUpdate(AIContext ctx)
        {
            ctx.StateTimer += ctx.DeltaTime;
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            if (ctx.StateTimer < ctx.Config.TauntDuration)
                return null;

            float distToTarget = ctx.Target != null
                ? Vector3.Distance(ctx.Self.position, ctx.Target.position)
                : float.MaxValue;

            if (distToTarget < ctx.Config.AttackRange)
                return MonsterAIState.Attack;
            if (ctx.Target != null)
                return MonsterAIState.Chase;
            return MonsterAIState.Idle;
        }

        public override void OnExit(AIContext ctx)
        {
            ctx.Movement.Resume();
        }
    }
}

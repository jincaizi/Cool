namespace Hotfix.GameSystems.Monster
{
    public class IdleState : AIStateBase
    {
        public override MonsterAIState StateType => MonsterAIState.Idle;

        private float _idleDuration;

        public override void OnEnter(AIContext ctx)
        {
            ctx.Movement.Stop();
            ctx.Animator.SetFloat(MonsterAnimHashes.Speed, 0f);
            ctx.StateTimer = 0f;
            _idleDuration = RandomRange(ctx.Config.IdleDuration, ctx.Config.IdleDurationVariance);
        }

        public override void OnUpdate(AIContext ctx)
        {
            ctx.StateTimer += ctx.DeltaTime;
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            float distToTarget = ctx.Target != null
                ? UnityEngine.Vector3.Distance(ctx.Self.position, ctx.Target.position)
                : float.MaxValue;

            if (distToTarget < ctx.Config.DetectRange)
                return MonsterAIState.Chase;

            if (ctx.StateTimer >= _idleDuration)
                return MonsterAIState.Patrol;

            return null;
        }

        private static float RandomRange(float baseValue, float variance)
        {
            if (variance <= 0) return baseValue;
            return baseValue + UnityEngine.Random.Range(-variance, variance);
        }
    }
}

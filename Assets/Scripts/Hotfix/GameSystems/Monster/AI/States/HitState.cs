using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // HitState duration is determined by HitReactLevel, looked up from config table.
    // During HitState: movement stopped, i-frame modifier active for configurable window.
    public class HitState : AIStateBase
    {
        private readonly DamagePipeline _pipeline;
        private float _hitDuration;

        public HitState(DamagePipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public override MonsterAIState StateType => MonsterAIState.Hit;

        public override void OnEnter(AIContext ctx)
        {
            // Determine stun duration from react level via config table
            int level = (int)ctx.LastHitResult.ReactLevel;
            var durations = ctx.Config.HitReactDurations;
            _hitDuration = (durations != null && level < durations.Length)
                ? durations[level]
                : 0.3f;

            ctx.StateTimer = 0f;
            ctx.Movement.Stop();
            ctx.Animator.SetTrigger(MonsterAnimHashes.Hit);

            // Enable brief invincibility window to prevent consecutive-hit stun-lock
            _pipeline.SetIFrameActive(true);
        }

        public override void OnUpdate(AIContext ctx)
        {
            ctx.StateTimer += ctx.DeltaTime;

            // Deactivate i-frames after the configured window passes
            if (ctx.StateTimer >= ctx.Config.HitIFrameDuration)
                _pipeline.SetIFrameActive(false);
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            if (ctx.StateTimer >= _hitDuration)
                return RecoverFromHit(ctx);

            return null;
        }

        public override void OnExit(AIContext ctx)
        {
            _pipeline.SetIFrameActive(false);
            ctx.Movement.ResetKnockback();
        }

        private MonsterAIState? RecoverFromHit(AIContext ctx)
        {
            if (ctx.Target != null)
            {
                float dist = Vector3.Distance(ctx.Self.position, ctx.Target.position);
                if (dist < ctx.Config.AttackRange)
                    return MonsterAIState.Attack;
                return MonsterAIState.Chase;
            }

            // No target: return to previous state, unless it was Hit or Death
            return ctx.PreviousState == MonsterAIState.Hit
                || ctx.PreviousState == MonsterAIState.Death
                ? MonsterAIState.Idle
                : ctx.PreviousState;
        }
    }
}

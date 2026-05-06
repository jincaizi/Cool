using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    public class DefendBehaviour : IAIBehaviour
    {
        private bool _counterReady;
        private float _defendCooldownTimer;

        public MonsterAIState StateType => MonsterAIState.Defend;

        public bool IsCounterReady => _counterReady;
        public float CooldownTimer => _defendCooldownTimer;

        public void SetCooldownTimer(float value) => _defendCooldownTimer = value;

        public bool CanEnter(MonsterAIContext ctx)
        {
            if (!ctx.Config.EnableDefend) return false;
            if (_defendCooldownTimer > 0) return false;

            float hpRatio = ctx.Stats.HP / ctx.Stats.MaxHP;
            bool hpCondition = hpRatio < ctx.Config.DefendHPThreshold;
            bool chaseCondition = ctx.DefendChaseTimer > ctx.Config.DefendChaseTimeThreshold;
            return hpCondition || chaseCondition;
        }

        public void Enter(MonsterAIContext ctx)
        {
            ctx.DefendBlockCount = 0;
            _counterReady = false;
            ctx.Movement.Stop();
            ctx.Animator.SetBool("IsDefending", true);
        }

        public void Update(MonsterAIContext ctx, float deltaTime)
        {
            _defendCooldownTimer -= deltaTime;
            if (ctx.Target != null)
                ctx.Movement.LookAt(ctx.Target.position);
            if (ctx.DefendBlockCount >= ctx.Config.DefendBlockCountToCounter)
                _counterReady = true;
        }

        public void Exit(MonsterAIContext ctx)
        {
            ctx.Animator.SetBool("IsDefending", false);
            ctx.Movement.Resume();
            _defendCooldownTimer = ctx.Config.DefendCooldown;
        }
    }
}

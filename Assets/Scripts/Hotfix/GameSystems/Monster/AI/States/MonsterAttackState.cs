using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    // MonsterAttackState lifecycle:
    //   Windup (timer < WindupTime) → Active (resolve damage) → Recovery (timer < total) → Exit
    //
    // Damage is resolved ONCE at the Windup→Active boundary.
    // Timer-based (not animation callback) for simplicity.
    // For frame-accurate timing, replace timer check with animation event callback in the future.
    public class MonsterAttackState : AIStateBase
    {
        private readonly List<IDamageable> _hitBuffer = new List<IDamageable>(8);
        private bool _damageDealt;
        private float _totalDuration;

        public override MonsterAIState StateType => MonsterAIState.Attack;

        public override void OnEnter(AIContext ctx)
        {
            _damageDealt = false;
            _totalDuration = ctx.Config.AttackWindupTime + ctx.Config.AttackRecoveryTime;
            ctx.StateTimer = 0f;
            ctx.Movement.Stop();

            if (ctx.Target != null)
                ctx.Movement.LookAt(ctx.Target.position);

            int attackIndex = PickAttackIndex(ctx.Config);
            ctx.Animator.SetInteger(MonsterAnimHashes.AttackIndex, attackIndex);
            ctx.Animator.SetTrigger(MonsterAnimHashes.Attack);
            ctx.AttackCooldown = RandomRange(ctx.Config.AttackCooldown, ctx.Config.AttackCooldownVariance);
        }

        public override void OnUpdate(AIContext ctx)
        {
            ctx.StateTimer += ctx.DeltaTime;

            if (ctx.Target != null)
                ctx.Movement.LookAt(ctx.Target.position);

            // Resolve damage at the windup→active boundary (once per attack)
            if (!_damageDealt && ctx.StateTimer >= ctx.Config.AttackWindupTime)
            {
                ResolveDamage(ctx);
                _damageDealt = true;
            }
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            // Stay in attack until timer reaches total duration
            if (ctx.StateTimer < _totalDuration)
                return null;

            // Attack finished. Check if taunt should trigger (random chance after attack).
            if (ctx.Config.EnableTaunt && UnityEngine.Random.value < ctx.Config.TauntChance)
                return MonsterAIState.Taunt;

            return MonsterAIState.Chase;
        }

        private void ResolveDamage(AIContext ctx)
        {
            var damage = ctx.Config.AttackDamage ?? DamageBlock.CreateDefault(ctx.Config.AttackPower);

            int mask = LayerMask.GetMask("Character");
            var shape = AttackShapeFactory.Create(ctx.Config.AttackShape, PhysicsRegistry.Instance, EntityType.Player);
            _hitBuffer.Clear();
            shape.ResolveNonAlloc(ctx.Self.position, ctx.Self.forward, mask, _hitBuffer);

            foreach (var t in _hitBuffer)
            {
                Vector3 dir = (t.Transform.position - ctx.Self.position).normalized;
                t.TakeDamage(damage, dir);
            }
        }

        private static int PickAttackIndex(MonsterConfig config)
        {
            if (config.AttackAnimCount <= 1) return 0;
            if (config.AttackWeights == null || config.AttackWeights.Length == 0) return 0;
            float roll = Random.value;
            float cumulative = 0;
            for (int i = 0; i < config.AttackWeights.Length && i < config.AttackAnimCount; i++)
            {
                cumulative += config.AttackWeights[i];
                if (roll <= cumulative) return i;
            }
            return 0;
        }

        private static float RandomRange(float baseValue, float variance)
        {
            if (variance <= 0) return baseValue;
            return baseValue + Random.Range(-variance, variance);
        }
    }
}

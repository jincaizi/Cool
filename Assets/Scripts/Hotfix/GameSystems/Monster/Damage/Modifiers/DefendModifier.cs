using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // Reduces damage from frontal attacks during Defend state.
    // Priority=100: runs after invincibility checks, before shields.
    // Read MonsterConfig.DefendAngle and DefendDamageReduction at runtime.
    public class DefendModifier : IDamageModifier
    {
        private readonly MonsterConfig _config;
        private readonly Transform _self;
        private readonly System.Func<bool> _isDefending;

        public int Priority => 100;

        public DefendModifier(MonsterConfig config, Transform self, System.Func<bool> isDefending)
        {
            _config = config;
            _self = self;
            _isDefending = isDefending;
        }

        public DamageResult Modify(ref DamageContext ctx)
        {
            // Passthrough when not actively defending — no reduction, full knockback
            if (!_isDefending())
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            if ((ctx.Flags & DamageFlags.IgnoresDefense) != 0)
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            float angle = Vector3.Angle(_self.forward, -ctx.HitDirection);
            if (angle >= _config.DefendAngle * 0.5f)
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            ctx.BlockCount++;
            float reducedDmg = ctx.CurrentDamage * (1f - _config.DefendDamageReduction);

            var result = new DamageResult
            {
                FinalDamage = reducedDmg <= 0 ? 0 : reducedDmg,
                WasReduced = true,
                WasBlocked = reducedDmg <= 0,
                ShouldKnockback = false,
                ReactLevel = HitReactLevel.None,
            };

            return result;
        }
    }
}

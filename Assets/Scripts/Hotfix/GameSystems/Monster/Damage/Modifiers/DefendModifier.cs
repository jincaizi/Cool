using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // Configuration values for defend behavior, extracted from monster/player configs.
    public struct DefendConfig
    {
        public float DamageReduction;  // 0..1, fraction of damage blocked (e.g. 0.8 = 80% reduction)
        public float DefendAngle;      // full-angle in degrees for frontal block check

        public static DefendConfig Default => new DefendConfig
        {
            DamageReduction = 0.8f,
            DefendAngle = 160f
        };
    }

    // Reduces damage from frontal attacks during defend state.
    // Priority=100: runs after invincibility checks, before shields.
    public class DefendModifier : IDamageModifier
    {
        private readonly DefendConfig _config;
        private readonly Transform _self;
        private readonly System.Func<bool> _isDefending;

        public int Priority => 100;

        public DefendModifier(DefendConfig config, Transform self, System.Func<bool> isDefending)
        {
            _config = config;
            _self = self;
            _isDefending = isDefending;
        }

        public DamageResult Modify(ref DamageContext ctx)
        {
            if (!_isDefending())
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            if ((ctx.Flags & DamageFlags.IgnoresDefense) != 0)
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            float angle = Vector3.Angle(_self.forward, -ctx.HitDirection);
            if (angle >= _config.DefendAngle * 0.5f)
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            ctx.BlockCount++;
            float reducedDmg = ctx.CurrentDamage * (1f - _config.DamageReduction);

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

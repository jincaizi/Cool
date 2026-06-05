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

        public int Priority => 100;

        public DefendModifier(MonsterConfig config, Transform self)
        {
            _config = config;
            _self = self;
        }

        public DamageResult Modify(ref DamageContext ctx)
        {
            var result = new DamageResult
            {
                FinalDamage = ctx.CurrentDamage,
                ShouldKnockback = true,
                ReactLevel = HitReactLevel.Flinch,
            };

            // IgnoresDefense flag bypasses all defense checks
            if ((ctx.Flags & DamageFlags.IgnoresDefense) != 0)
                return result;

            // Only active during Defend state — caller checks state before invoking pipeline.
            // If the hit comes from behind or outside the defend cone, defense is bypassed entirely.
            float angle = Vector3.Angle(_self.forward, -ctx.HitDirection);
            if (angle >= _config.DefendAngle * 0.5f)
                return result;

            // Frontal hit: reduce damage and suppress knockback
            ctx.BlockCount++;
            result.FinalDamage = ctx.CurrentDamage * (1f - _config.DefendDamageReduction);
            result.WasReduced = true;
            result.ShouldKnockback = false;
            result.ReactLevel = HitReactLevel.None;

            if (result.FinalDamage <= 0)
            {
                result.FinalDamage = 0;
                result.WasBlocked = true;
            }

            return result;
        }
    }
}

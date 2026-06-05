namespace Hotfix.GameSystems.Monster
{
    // Brief invincibility window after being hit.
    // Priority=0: runs first to block all damage during i-frame window.
    // Timer is managed externally — AIBrain or HitState sets Active=true/false.
    public class IFrameModifier : IDamageModifier
    {
        public bool Active { get; set; }

        public int Priority => 0;

        public DamageResult Modify(ref DamageContext ctx)
        {
            if (!Active)
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            return new DamageResult
            {
                FinalDamage = 0,
                WasBlocked = true,
                ShouldKnockback = false,
                ReactLevel = HitReactLevel.None,
            };
        }
    }
}

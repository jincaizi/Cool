namespace Hotfix.GameSystems.Monster
{
    // Output of IDamageModifier.Modify() and the DamagePipeline.
    // Each modifier returns a result; the pipeline merges them (last non-default value wins).
    public struct DamageResult
    {
        public float FinalDamage;
        public bool WasBlocked;       // Zero damage — skip VFX entirely
        public bool WasReduced;       // Partial damage — play "glancing hit" VFX
        public bool PreventDeath;     // "Survive with 1 HP" buff support (future)
        public bool ShouldKnockback;  // Boss super armor sets this to false
        public HitReactLevel ReactLevel;
    }

    // Determines which hit state the monster enters, and for how long.
    // Index into MonsterConfig.HitReactDurations[] for timing.
    // Add new levels for future knockup/launch/pull mechanics.
    public enum HitReactLevel
    {
        None = 0,       // Boss super armor — no hit state transition
        Flinch = 1,     // Brief interrupt for light attacks
        Stagger = 2,    // Medium stun for heavy attacks
        Knockback = 3,  // Push back with displacement
        Launch = 4,     // Airborne (reserved for future launcher skills)
    }
}

using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // struct: stack-allocated, zero GC pressure.
    // Passed by ref through the pipeline so modifiers can mutate CurrentDamage.
    public struct DamageContext
    {
        // ── Input (set by caller, read-only for modifiers) ──
        public Skills.Data.DamageBlock RawData;
        public Vector3 HitDirection;
        public int AttackerId;
        public DamageFlags Flags;

        // ── Mutated by modifiers ──
        public float CurrentDamage;
        public int BlockCount;

        // When set by caller, used as the damage amount instead of reading BaseDamage.
        // This allows SkillExecutor to pass the post-scaling calculated damage.
        public float OverrideDamage;

        public float RawDamage => OverrideDamage > 0 ? OverrideDamage : (RawData?.BaseDamage ?? 0f);
    }

    // Flags allow modifiers to branch behavior without type-checking each damage source.
    // Add new flags for future damage types (e.g., TrueDamage, Heal).
    [System.Flags]
    public enum DamageFlags
    {
        None = 0,
        IsDoT = 1 << 0,         // Damage-over-time tick — no hit reaction, minimal VFX
        IsCritical = 1 << 1,    // Critical hit — special VFX/float text
        IgnoresDefense = 1 << 2,// Bypasses DefendModifier and armor
        IsReflected = 1 << 3,   // Reflected/thorns damage — prevents infinite loops
    }
}

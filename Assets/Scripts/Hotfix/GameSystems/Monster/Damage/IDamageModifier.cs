namespace Hotfix.GameSystems.Monster
{
    // Design: Each defense mechanism is a pluggable IDamageModifier.
    // Modifiers are checked in priority order (lowest first).
    //
    // To add a new defense type (e.g., ShieldModifier, MagicArmorModifier):
    // 1. Create a class implementing this interface
    // 2. Assign a priority (Defend=100, Shield=200, Invincible=0)
    // 3. Register it in DamagePipeline's modifier list
    // No other code changes needed.
    //
    // Priority conventions:
    //   0   = Invincibility (must run first to block all damage)
    //   100 = Defense (armor/damage reduction)
    //   200 = Shield (absorbs damage after reduction)
    //   300 = Thorns (reflects remaining damage)
    public interface IDamageModifier
    {
        // Lower values execute first. Modifiers at the same priority execute in registration order.
        int Priority { get; }

        // Mutate ctx.CurrentDamage and return the modified result.
        // ctx is passed by ref so modifiers can read/write shared state (BlockCount, etc.).
        DamageResult Modify(ref DamageContext ctx);
    }
}

using Cool.SyncCore;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.VFX
{
    public static class ElementVfxAssets
    {
        public static string SwordQiKey(int elementId)
            => ElementalCatalogDefaults.FindElement(elementId)?.SwordQiViewKey ?? string.Empty;

        public static string StatusKey(EffectType type) => type switch
        {
            EffectType.Freeze => "status/ice",
            EffectType.Burn => "status/fire",
            EffectType.Stun => "status/thunder",
            EffectType.Vulnerable => "status/wind",
            _ => string.Empty
        };
    }
}

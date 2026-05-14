using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public struct NameplateConfig
    {
        public string DisplayName;
        public Color NameColor;
        public Sprite ClassIcon;
        public float VerticalOffset;
        public float CullDistance;

        public NameplateConfig(string displayName, Color? color = null)
        {
            DisplayName = displayName;
            NameColor = color ?? Color.white;
            ClassIcon = null;
            VerticalOffset = 2.5f;
            CullDistance = 0f; // 0 = use global default
        }
    }
}

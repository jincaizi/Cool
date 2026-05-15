using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public struct NameplateConfig
    {
        public string DisplayName;
        public Color? NameColor;
        public Sprite ClassIcon;

        public NameplateConfig(string displayName, Color? color = null)
        {
            DisplayName = displayName;
            NameColor = color;
            ClassIcon = null;
        }
    }
}

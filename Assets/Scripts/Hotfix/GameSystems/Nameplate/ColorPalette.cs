using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public static class ColorPalette
    {
        // Entity nameplate colors
        public static readonly Color Npc = Color.white;
        public static readonly Color Monster = new Color(1f, 0.5f, 0.3f);
        public static readonly Color Player = new Color(0.3f, 1f, 0.3f);

        // Profession colors
        public static readonly Color Warrior = new Color(0.85f, 0.33f, 0.28f);
        public static readonly Color Mage = new Color(0.28f, 0.55f, 1f);
        public static readonly Color Priest = Color.white;
        public static readonly Color Rogue = new Color(1f, 0.9f, 0.3f);

        public static Color ForProfession(int professionId)
        {
            return professionId switch
            {
                1 => Warrior,
                2 => Mage,
                3 => Priest,
                4 => Rogue,
                _ => Npc
            };
        }
    }
}

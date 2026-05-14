using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public enum FloatTextType
    {
        Normal,
        Crit,
        Heal,
        Dodge,
        Block,
        DOT,
        SkillName
    }

    public class FloatTextConfig
    {
        public FloatTextType Type;
        public Color Color = Color.white;
        public float FontSize = 36f;
        public float Duration = 1f;
        public float MoveUpDistance = 50f;
        public bool ShowName;
        public string TextOverride;
    }

    public static class FloatTextPresets
    {
        public static FloatTextConfig Damage => new()
        {
            Type = FloatTextType.Normal,
            Color = new Color(1f, 0.27f, 0.27f),
            FontSize = 36f,
            Duration = 0.8f,
            MoveUpDistance = 50f
        };

        public static FloatTextConfig CritDamage => new()
        {
            Type = FloatTextType.Crit,
            Color = new Color(1f, 0.53f, 0f),
            FontSize = 42f,
            Duration = 1.2f,
            MoveUpDistance = 70f
        };

        public static FloatTextConfig Heal => new()
        {
            Type = FloatTextType.Heal,
            Color = new Color(0.27f, 1f, 0.27f),
            FontSize = 32f,
            Duration = 1f,
            MoveUpDistance = 40f
        };

        public static FloatTextConfig Dodge => new()
        {
            Type = FloatTextType.Dodge,
            Color = Color.white,
            FontSize = 28f,
            Duration = 0.6f,
            MoveUpDistance = 40f,
            TextOverride = "闪避"
        };

        public static FloatTextConfig Block => new()
        {
            Type = FloatTextType.Block,
            Color = new Color(1f, 0.84f, 0f),
            FontSize = 28f,
            Duration = 0.6f,
            MoveUpDistance = 40f,
            TextOverride = "格挡"
        };

        public static FloatTextConfig DOT => new()
        {
            Type = FloatTextType.DOT,
            Color = new Color(0.8f, 0.8f, 0.8f),
            FontSize = 22f,
            Duration = 0.5f,
            MoveUpDistance = 20f
        };

        public static FloatTextConfig SkillName(string name) => new()
        {
            Type = FloatTextType.SkillName,
            Color = new Color(1f, 0.84f, 0f),
            FontSize = 28f,
            Duration = 1.5f,
            MoveUpDistance = 20f,
            ShowName = true,
            TextOverride = name
        };
    }
}

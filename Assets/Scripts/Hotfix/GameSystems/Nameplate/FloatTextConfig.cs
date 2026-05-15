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
        public FloatTextSettings Settings;
        public string TextOverride;
        public bool ShowName;
    }
}

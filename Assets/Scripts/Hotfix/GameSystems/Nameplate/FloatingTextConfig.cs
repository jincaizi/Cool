using System;
using DG.Tweening;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    [Serializable]
    public class FloatingTextConfig
    {
        public Color Color = Color.white;
        public float FontSize = 36f;
        public float Duration = 1f;
        public float MoveUpDistance = 50f;
        public float StartScale = 1f;
        public bool PunchScale;
        public Ease Ease = Ease.OutCubic;
    }

    public static class FloatingTextPresets
    {
        public static readonly FloatingTextConfig Damage = new()
        {
            Color = new Color(1f, 0.27f, 0.27f),
            FontSize = 36f,
            Duration = 1f,
            MoveUpDistance = 50f,
            Ease = Ease.OutCubic
        };

        public static readonly FloatingTextConfig CritDamage = new()
        {
            Color = new Color(1f, 0.53f, 0f),
            FontSize = 42f,
            Duration = 1.2f,
            MoveUpDistance = 70f,
            PunchScale = true,
            Ease = Ease.OutBack
        };

        public static readonly FloatingTextConfig Heal = new()
        {
            Color = new Color(0.27f, 1f, 0.27f),
            FontSize = 32f,
            Duration = 1f,
            MoveUpDistance = 40f,
            Ease = Ease.OutCubic
        };

        public static readonly FloatingTextConfig SkillName = new()
        {
            Color = new Color(1f, 0.84f, 0f),
            FontSize = 28f,
            Duration = 1.5f,
            MoveUpDistance = 20f,
            StartScale = 0.8f,
            Ease = Ease.OutCubic
        };
    }
}

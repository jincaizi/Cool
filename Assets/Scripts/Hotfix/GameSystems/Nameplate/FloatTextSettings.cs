using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    [CreateAssetMenu(menuName = "Display/FloatTextSettings", fileName = "FloatTextSettings")]
    public class FloatTextSettings : ScriptableObject
    {
        public FloatTextType Type;
        public TMP_FontAsset Font;
        public Material FontMaterial;
        public float FontSize = 36f;
        public Color Color = Color.white;
        public float Duration = 1f;
        public float MoveUpDistance = 50f;
        [Range(0f, 1f)] public float FadeStartRatio = 0.5f;
        public float StartScale = 1f;
    }
}

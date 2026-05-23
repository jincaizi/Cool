using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    [CreateAssetMenu(menuName = "Display/NameplateSettings", fileName = "NameplateSettings")]
    public class NameplateSettings : ScriptableObject
    {
        public TMP_FontAsset Font;
        public Material FontMaterial;
        public float FontSize = 18f;
        public Color DefaultColor = Color.white;
        public float OutlineWidth = 0.15f;
        public Color OutlineColor = Color.black;
        public float VerticalOffset = 1.2f;
        public float CullDistance = 50f;
        public float FadeStartDistance = 30f;
        public Vector2 IconSize = new(20, 20);
    }
}

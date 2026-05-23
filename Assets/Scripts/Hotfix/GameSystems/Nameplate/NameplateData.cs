using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    /// <summary>
    /// 怪物昵称样式配置表（ScriptableObject）
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Display/Monster Nameplate Data")]
    public class NameplateData : ScriptableObject
    {
        [Header("Style")]
        public Color NameColor = Color.white;
        public Sprite ClassIcon;

        [Header("Font")]
        public TMP_FontAsset Font;
        public Material FontMaterial;
        public float FontSize = 18f;

        [Header("Position")]
        public float VerticalOffset = 1.2f;

        [Header("Visibility")]
        public bool ShowHPBar = true;
        public bool ShowLevel = false;
    }
}
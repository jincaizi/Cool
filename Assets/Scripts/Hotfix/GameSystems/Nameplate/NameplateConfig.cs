using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    /// <summary>
    /// 昵称配置（运行时数据结构）
    /// </summary>
    public struct NameplateConfig
    {
        public string DisplayName;
        public Color? NameColor;
        public Sprite ClassIcon;
        public TMP_FontAsset Font;
        public Material FontMaterial;
        public float FontSize;
        public float VerticalOffset; // < 0 表示使用全局设置
        public bool ShowHPBar;
        public bool ShowLevel;

        public NameplateConfig(string displayName, Color? color = null, Sprite classIcon = null,
            TMP_FontAsset font = null, Material fontMaterial = null, float fontSize = 18f,
            float verticalOffset = -1f, bool showHPBar = true, bool showLevel = false)
        {
            DisplayName = displayName;
            NameColor = color;
            ClassIcon = classIcon;
            Font = font;
            FontMaterial = fontMaterial;
            FontSize = fontSize;
            VerticalOffset = verticalOffset;
            ShowHPBar = showHPBar;
            ShowLevel = showLevel;
        }

        /// <summary>
        /// 从 NameplateData 创建（样式配置，名字由调用方提供）
        /// </summary>
        public static NameplateConfig FromData(NameplateData data, string displayName)
        {
            if (data != null)
            {
                return new NameplateConfig(
                    displayName,
                    data.NameColor,
                    data.ClassIcon,
                    data.Font,
                    data.FontMaterial,
                    data.FontSize,
                    data.VerticalOffset,
                    data.ShowHPBar,
                    data.ShowLevel);
            }
            return new NameplateConfig(displayName);
        }
    }
}
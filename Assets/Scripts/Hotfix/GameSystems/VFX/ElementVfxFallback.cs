using System;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 元素 VFX 表现回退：ViewKey 命中资源时由 Host 实例化并使用资源自带颜色；
    /// 否则回退到 <see cref="ElementPalette"/> 的程序化颜色。不直接调用 Addressables
    /// （生产加载器由 Plan 06 的 Host 注入）。
    /// </summary>
    public static class ElementVfxFallback
    {
        public static Color ResolveColor(int elementId, string viewKey, Func<string, GameObject?>? loader)
        {
            if (!string.IsNullOrEmpty(viewKey) && loader != null && loader(viewKey) != null)
                return Color.white; // 资源自带颜色，Host 负责实例化
            return ElementPalette.ColorOf(elementId);
        }
    }
}

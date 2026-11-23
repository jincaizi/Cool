using System;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 元素 VFX 的颜色选择规则。资源可用时保留资源自带颜色，
    /// 其余情况使用 <see cref="ElementPalette"/>。游戏内的异步表现由
    /// <see cref="ElementVfxAttachment"/> 管理。
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

using UnityEngine;

namespace Hotfix.GameSystems.UI.Framework.Core
{
    public static class RectTransformExtensions
    {
        /// <summary>
        /// Stretch the RectTransform to fill its parent.
        /// </summary>
        public static void StretchParent(this RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
        }
    }
}
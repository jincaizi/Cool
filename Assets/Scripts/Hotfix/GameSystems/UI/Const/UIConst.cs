namespace Hotfix.GameSystems.UI
{
    public enum LayerType
    {
        Base   = 0,
        Main   = 1,
        Popup  = 2,
        Top    = 3,
        Guide  = 4
    }

    public enum VisibilityMode
    {
        ToggleActive,
        CanvasSwitch,
        CanvasGroup
    }

    public static class UIConst
    {
        /// <summary>
        /// 层定义的唯一真值源：**行序即层序，数组下标必须等于 (int)LayerType 的值**。
        /// UIManager 按本表建 Canvas —— 层的数量、命名、排序号全部来自这里，
        /// 不再有第二个地方写 "5" 或排序号。
        /// 不变量由 UILayerTableTests 守住。
        /// </summary>
        public static readonly (LayerType layer, int sortOrder)[] Layers =
        {
            (LayerType.Base,  1000),
            (LayerType.Main,  2000),
            (LayerType.Popup, 3000),
            (LayerType.Top,   4000),
            (LayerType.Guide, 5000),
        };
    }
}

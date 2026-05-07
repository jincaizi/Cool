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
        public static readonly int[] SortOrders =
        {
            1000,   // Base
            2000,   // Main
            3000,   // Popup
            4000,   // Top
            5000    // Guide
        };
    }
}

namespace Hotfix.GameSystems.UI.Framework.Core
{
    /// <summary>
    /// UI layer constants for sort order separation.
    /// Each layer is an independent Canvas.
    /// </summary>
    public static class UIConst
    {
        // Layer sort order ranges
        public const int Layer_Base = 0;
        public const int Layer_Main = 1000;
        public const int Layer_Popup = 2000;
        public const int Layer_Guide = 3000;
        public const int Layer_Toast = 4000;

        // Layer canvas names (for hierarchy organization)
        public const string Canvas_Base = "Canvas_Base";
        public const string Canvas_Main = "Canvas_Main";
        public const string Canvas_Popup = "Canvas_Popup";
        public const string Canvas_Guide = "Canvas_Guide";
        public const string Canvas_Toast = "Canvas_Toast";

        // Default animation durations
        public const float DefaultAnimDuration = 0.3f;

        // Pool defaults
        public const int DefaultPreLoadCount = 3;
    }
}

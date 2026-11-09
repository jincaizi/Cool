using UnityEngine;
using UnityEngine.UI;
using DataDefinition;

namespace Hotfix.GameSystems.UI
{
    public class ScreenAdapter : MonoBehaviour
    {
        public bool ApplySafeArea = true;
        public bool AutoRefreshOnResize = true;

        private CanvasScaler _scaler;
        private RectTransform _rectTransform;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            _rectTransform = GetComponent<RectTransform>();
            ConfigureCanvasScaler();
        }

        private void Start()
        {
            if (ApplySafeArea)
                ApplySafeAreaOffset();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (AutoRefreshOnResize && ApplySafeArea)
                ApplySafeAreaOffset();
        }

        /// <summary>
        /// 浏览器地址栏伸缩、横竖屏切换可能只改安全区域而不触发 RectTransform 尺寸变化。
        /// 每帧比较一次缓存值，变化才重算（成本可忽略）。
        /// </summary>
        private void Update()
        {
            if (!AutoRefreshOnResize || !ApplySafeArea) return;

            var screenSize = new Vector2Int(Screen.width, Screen.height);
            var safeArea = Screen.safeArea;
            if (screenSize == _lastScreenSize && safeArea == _lastSafeArea) return;

            _lastScreenSize = screenSize;
            _lastSafeArea = safeArea;
            ApplySafeAreaOffset();
        }

        private void ConfigureCanvasScaler()
            => ApplyReferenceResolution(_scaler, DataDefinition.GameSettings.Instance);

        internal static void ApplyReferenceResolution(
            CanvasScaler scaler, DataDefinition.GameSettings settings)
        {
            if (scaler == null || settings == null) return;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = settings.ResolveReferenceResolution();
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void ApplySafeAreaOffset()
        {
            if (_rectTransform == null || Screen.width <= 0 || Screen.height <= 0) return;

            var safeArea = Screen.safeArea;
            var screenSize = new Vector2(Screen.width, Screen.height);

            var anchorMin = safeArea.position / screenSize;
            var anchorMax = (safeArea.position + safeArea.size) / screenSize;

            _rectTransform.anchorMin = anchorMin;
            _rectTransform.anchorMax = anchorMax;
        }
    }
}

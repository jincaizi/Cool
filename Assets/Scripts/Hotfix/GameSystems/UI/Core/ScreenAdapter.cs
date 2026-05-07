using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class ScreenAdapter : MonoBehaviour
    {
        public bool ApplySafeArea = true;
        public bool AutoRefreshOnResize = true;

        private CanvasScaler _scaler;
        private RectTransform _rectTransform;

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

        private void ConfigureCanvasScaler()
        {
            if (_scaler == null) return;

            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920, 1080);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;
        }

        private void ApplySafeAreaOffset()
        {
            var safeArea = Screen.safeArea;
            var screenSize = new Vector2(Screen.width, Screen.height);

            var anchorMin = safeArea.position / screenSize;
            var anchorMax = (safeArea.position + safeArea.size) / screenSize;

            _rectTransform.anchorMin = anchorMin;
            _rectTransform.anchorMax = anchorMax;
        }
    }
}

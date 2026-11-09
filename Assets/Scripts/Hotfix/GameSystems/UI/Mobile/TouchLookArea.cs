using UnityEngine;
using UnityEngine.EventSystems;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 触屏观察区：只接管真实触摸；从按下位置量起始死区，鼠标事件完全忽略。
    /// </summary>
    public class TouchLookArea : MonoBehaviour, IPointerDownHandler,
        IInitializePotentialDragHandler, IDragHandler, IPointerUpHandler, IEndDragHandler
    {
        [SerializeField] private float _sensitivity = 0.2f;
        [Tooltip("起始拖拽死区（160dpi 基准像素数，运行时按 Screen.dpi 换算）")]
        [SerializeField] private float _minDragPixelsAt160Dpi = 12f;

        private Vector2 _pending;
        private Vector2 _delta;
        private int _activePointerId = -1;
        private Vector2 _pressPosition;
        private Vector2 _lastPosition;
        private bool _dragStarted;

        public Vector2 Delta => _delta;

        internal static float ResolveDragThresholdPixels(float minPixelsAt160Dpi, float dpi)
        {
            float safeDpi = dpi > 0f && float.IsFinite(dpi) ? dpi : 160f;
            return minPixelsAt160Dpi * Mathf.Max(1f, safeDpi / 160f);
        }

        private float DragThresholdPixels
            => ResolveDragThresholdPixels(_minDragPixelsAt160Dpi, Screen.dpi);

        internal static bool IsTouchPointer(PointerEventData e)
            => e.pointerId >= 0 && e.button == PointerEventData.InputButton.Left;

        private bool OwnsPointer(PointerEventData e)
            => _activePointerId >= 0 && IsTouchPointer(e)
                && e.pointerId == _activePointerId;

        public void OnPointerDown(PointerEventData e)
        {
            if (!IsTouchPointer(e) || _activePointerId >= 0) return;

            ResetGesture();
            _activePointerId = e.pointerId;
            _pressPosition = e.position;
            _lastPosition = e.position;
        }

        public void OnInitializePotentialDrag(PointerEventData e)
        {
            if (OwnsPointer(e)) e.useDragThreshold = false;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!OwnsPointer(e)) return;

            if (!_dragStarted)
            {
                Vector2 total = e.position - _pressPosition;
                float threshold = DragThresholdPixels;
                if (total.sqrMagnitude < threshold * threshold) return;

                _dragStarted = true;
                _pending += total * _sensitivity;
            }
            else
            {
                _pending += (e.position - _lastPosition) * _sensitivity;
            }

            _lastPosition = e.position;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (OwnsPointer(e)) ResetGesture();
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (OwnsPointer(e)) ResetGesture();
        }

        private void OnDisable() => ResetGesture();

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ResetGesture();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) ResetGesture();
        }

        private void ResetGesture()
        {
            _pending = Vector2.zero;
            _delta = Vector2.zero;
            _activePointerId = -1;
            _pressPosition = Vector2.zero;
            _lastPosition = Vector2.zero;
            _dragStarted = false;
        }

        /// <summary>
        /// 帧末发布本帧累积的拖拽量并清空。手指停住时 OnDrag 不再触发，
        /// 若无此步 Delta 会保持上一次的值，视角会持续旋转。
        /// </summary>
        private void LateUpdate()
        {
            _delta = _pending;
            _pending = Vector2.zero;
        }
    }
}

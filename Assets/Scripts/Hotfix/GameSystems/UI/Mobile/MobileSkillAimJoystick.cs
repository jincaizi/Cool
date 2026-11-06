using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    /// <summary>火球按钮上的瞄准摇杆：只产出瞄准状态与释放类型，不动技能数据。</summary>
    public class MobileSkillAimJoystick : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform _buttonRect;
        [SerializeField] private RectTransform _icon;
        [SerializeField] private float _aimRadius = 90f;      // Canvas 单位
        [SerializeField] private float _deadZone = 0.1f;
        [SerializeField] private float _cancelEnterScale = 1.4f;
        [SerializeField] private float _cancelExitScale = 1.2f;
        [SerializeField] private Color _cancelColor = Color.red;
        [SerializeField] private Graphic _cancelMark;

        private AimGestureState _state;
        private Vector2 _iconHome;
        private Image _iconImage;
        private Color _iconColor = Color.white;
        private AimReleaseKind _releaseKind;

        public bool Interactable { get; set; } = true;
        public RectTransform Icon => _icon;
        public bool IsGestureActive => _state != null && _state.IsActive;
        public Vector2 Aim => _state != null ? _state.Value : Vector2.zero;
        public bool CancelArmed => _state != null && _state.CancelArmed;

        private void Awake()
        {
            _state = new AimGestureState(_aimRadius, _deadZone, _cancelEnterScale, _cancelExitScale);
            if (_icon != null)
            {
                _iconHome = _icon.anchoredPosition;
                _iconImage = _icon.GetComponent<Image>();
                if (_iconImage != null) _iconColor = _iconImage.color;
            }
        }

        private void OnEnable()
        {
            _releaseKind = AimReleaseKind.None;
            if (_state != null) _state.Reset();
            ApplyVisual();
        }

        private void OnDisable()
        {
            if (_state != null) _state.Reset();
            _releaseKind = AimReleaseKind.Cancel;
            ApplyVisual();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactable || _buttonRect == null || _state == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _buttonRect, eventData.position, eventData.pressEventCamera, out var local);
            _state.Press(local);
            _releaseKind = AimReleaseKind.None;
            ApplyVisual();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_state == null || !_state.IsActive || _buttonRect == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _buttonRect, eventData.position, eventData.pressEventCamera, out var local);
            _state.Drag(local);
            ApplyVisual();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_state == null) return;
            _releaseKind = _state.Release();
            ApplyVisual();
        }

        public AimReleaseKind ConsumeReleaseKind()
        {
            var kind = _releaseKind;
            _releaseKind = AimReleaseKind.None;
            return kind;
        }

        /// <summary>技能已结束但手指仍按着时复位；不合成释放。</summary>
        public void ResetGesture()
        {
            if (_state != null) _state.Reset();
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            if (_icon != null)
                _icon.anchoredPosition = _iconHome + (_state != null ? _state.IconOffset : Vector2.zero);

            if (_iconImage != null)
                _iconImage.color = _state != null && _state.CancelArmed ? _cancelColor : _iconColor;

            if (_cancelMark != null)
                _cancelMark.enabled = _state != null && _state.CancelArmed;
        }
    }
}

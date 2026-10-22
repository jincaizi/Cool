using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 虚拟摇杆。Value 是**归一化前的**方向（模长 0–1），
    /// 由 JoystickInputAdapter 读取 —— 它负责死区，本类只管几何。
    /// 满推冲刺手势也在这里判定，输出 IsSprintHeld。
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform _background;
        [SerializeField] private RectTransform _handle;
        [SerializeField] private float _radius = 80f;

        [Header("Sprint Gesture")]
        [SerializeField] private float _sprintEnterThreshold = 0.9f;
        [SerializeField] private float _sprintExitThreshold = 0.8f;
        [SerializeField] private float _sprintHoldSeconds = 0.2f;
        [SerializeField, Tooltip("可选：冲刺时变色的 Graphic（手柄或外环）；为空则只有逻辑")]
        private Graphic _sprintIndicator;
        [SerializeField] private Color _sprintColor = new Color(1f, 0.6f, 0.1f, 1f);

        private Vector2 _value;
        private SprintGestureState _sprintState;
        private Color _idleColor;
        private bool _sprintVisual;

        public Vector2 Value => _value;
        public bool IsSprintHeld => _sprintState != null && _sprintState.IsSprinting;

        private void Awake()
        {
            _sprintState = new SprintGestureState(
                _sprintEnterThreshold, _sprintExitThreshold, _sprintHoldSeconds);
            if (_sprintIndicator != null) _idleColor = _sprintIndicator.color;
        }

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _background, e.position, e.pressEventCamera, out Vector2 local);
            Vector2 clamped = Vector2.ClampMagnitude(local, _radius);
            _handle.anchoredPosition = clamped;
            _value = clamped / _radius;        // 0–1 的模拟量，**不 normalize**
        }

        public void OnPointerUp(PointerEventData e)
        {
            _value = Vector2.zero;
            _handle.anchoredPosition = Vector2.zero;
            ResetSprint();
        }

        /// <summary>
        /// 每帧推进冲刺手势 —— 手指按住不动时不会再有 OnDrag，
        /// 只在事件里计时会永远进不了冲刺。
        /// </summary>
        private void Update()
        {
            if (_sprintState == null) return;
            ApplySprintVisual(_sprintState.Tick(_value.magnitude, Time.deltaTime));
        }

        /// <summary>
        /// 停用/失焦时清空 —— 浏览器抢焦点会让抬指事件丢失，
        /// 此时 OnPointerUp 不会来，Value 会锁存 ⇒ 角色一直跑。
        /// </summary>
        private void OnDisable()
        {
            _value = Vector2.zero;
            // 场景卸载/teardown 时子 RectTransform 可能先被销毁（本条路径可达，故判空）
            if (_handle != null) _handle.anchoredPosition = Vector2.zero;
            ResetSprint();
        }

        private void ResetSprint()
        {
            if (_sprintState != null) _sprintState.Reset();
            ApplySprintVisual(false);
        }

        private void ApplySprintVisual(bool sprinting)
        {
            if (_sprintVisual == sprinting) return;
            _sprintVisual = sprinting;
            if (_sprintIndicator != null)
                _sprintIndicator.color = sprinting ? _sprintColor : _idleColor;
        }
    }
}

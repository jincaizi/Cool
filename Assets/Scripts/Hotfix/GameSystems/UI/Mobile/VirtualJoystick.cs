using UnityEngine;
using UnityEngine.EventSystems;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 虚拟摇杆。Value 是**归一化前的**方向（模长 0–1），
    /// 由 JoystickInputAdapter 读取 —— 它负责死区，本类只管几何。
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform _background;
        [SerializeField] private RectTransform _handle;
        [SerializeField] private float _radius = 80f;

        private Vector2 _value;
        public Vector2 Value => _value;

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
        }

        /// <summary>
        /// 停用/失焦时清空 —— 浏览器抢焦点会让抬指事件丢失，
        /// 此时 OnPointerUp 不会来，Value 会锁存 ⇒ 角色一直跑。
        /// </summary>
        private void OnDisable()
        {
            _value = Vector2.zero;
            _handle.anchoredPosition = Vector2.zero;
        }
    }
}

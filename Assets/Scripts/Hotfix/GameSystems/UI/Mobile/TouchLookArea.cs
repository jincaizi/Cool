using UnityEngine;
using UnityEngine.EventSystems;

namespace Hotfix.GameSystems.UI
{
    /// <summary>触屏观察区：拖拽量（像素/帧）由 JoystickInputAdapter 读取。</summary>
    public class TouchLookArea : MonoBehaviour, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private float _sensitivity = 0.2f;

        private Vector2 _pending;
        private Vector2 _delta;
        public Vector2 Delta => _delta;

        public void OnDrag(PointerEventData e)
        {
            _pending += e.delta * _sensitivity;
        }

        public void OnPointerUp(PointerEventData e)
        {
            _pending = Vector2.zero;
            _delta = Vector2.zero;
        }

        /// <summary>
        /// 帧末发布本帧累积的拖拽量并清空。手指停住时 OnDrag 不再触发，
        /// 若无此步 Delta 会一直保持上一次的值，而 Sys3CEntry 每帧都读它 → 视角会持续旋转不停。
        /// 放在 LateUpdate（所有 Update 之后）而非 Update，是为了与 EventSystem 的处理顺序无关：
        /// 本帧写入的量必定在下一次读取前发布，不会因脚本执行顺序而丢帧。
        /// </summary>
        private void LateUpdate()
        {
            _delta = _pending;
            _pending = Vector2.zero;
        }
    }
}

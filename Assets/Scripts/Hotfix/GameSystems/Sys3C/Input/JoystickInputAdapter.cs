using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 移动端输入适配器 —— 从 UI 摇杆与触屏观察区取输入。
    /// **重写原因**（见 Task 6 说明）：原实现读三个不存在的 legacy Input 轴
    /// （GetAxisRaw 对未定义轴会抛异常），且 normalized 丢弃模拟量。
    ///
    /// 本类实现既有的 IInputAdapter 契约，成员签名未变 ——
    /// InputManager 与 KeyboardInputAdapter 都不受影响。
    /// 输入源用委托注入，因此本类可在 EditMode 单测（不需要真实 UI）。
    /// </summary>
    public class JoystickInputAdapter : IInputAdapter
    {
        private const float DeadZone = 0.05f;

        private readonly Func<Vector2> _moveSource;
        private readonly Func<Vector2> _lookSource;
        private readonly IInputAdapter _fallback;

        public string AdapterName => "Joystick";

        public JoystickInputAdapter(Func<Vector2> moveSource, Func<Vector2> lookSource,
            IInputAdapter fallback = null)
        {
            _moveSource = moveSource;
            _lookSource = lookSource;
            _fallback = fallback;
        }

        /// <summary>
        /// 摇杆输入 → 世界方向。
        /// **不 normalize**：保留模拟量，推一半走一半（P6 审查记录的坑）。
        /// 死区以下的抖动视为无输入；此时回退到 fallback（PC/Editor 上的 WASD）。
        /// </summary>
        public Vector3 GetMoveInput()
        {
            Vector2 v = _moveSource();
            if (v.sqrMagnitude < DeadZone * DeadZone)
                return _fallback != null ? _fallback.GetMoveInput() : Vector3.zero;
            return new Vector3(v.x, 0f, v.y);   // 注意是 v.y → z（俯视平面）
        }

        /// <summary>
        /// 触屏拖拽量原样透传（灵敏度由相机侧既有参数负责，不在此再缩放）；
        /// 触屏空闲时回退到 fallback（PC/Editor 上的鼠标视角）。
        /// </summary>
        public Vector2 GetCameraRotationInput()
        {
            Vector2 look = _lookSource();
            if (look.sqrMagnitude > 0f) return look;
            return _fallback != null ? _fallback.GetCameraRotationInput() : Vector2.zero;
        }

        public bool HasMoveInput() => GetMoveInput().sqrMagnitude > 0f;

        public bool HasCameraRotationInput() => GetCameraRotationInput().sqrMagnitude > 0f;
    }
}

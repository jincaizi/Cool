using UnityEngine;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.UI;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 移动端输入接线 —— 只在触屏形态生效（WebGL 跑在手机上即为触屏）。
    /// PC 浏览器与编辑器维持键鼠，不影响既有验收。
    ///
    /// 本组件必须挂在**常驻激活**的对象上：`_mobileUiRoot` 默认不激活，
    /// 若挂在它自身之下，`Update` 永不执行，手机上 UI 永远不会出现。
    /// </summary>
    public class MobileInputBinder : MonoBehaviour
    {
        [SerializeField] private VirtualJoystick _joystick;
        [SerializeField] private TouchLookArea _lookArea;
        [SerializeField] private MobileActionButtons _buttons;
        [SerializeField] private GameObject _mobileUiRoot;

        /// <summary>
        /// 触屏判定的人工档位。`Auto` 走自动判定；`ForceTouch` / `ForceNoTouch` 直接压过自动判定
        /// （URL `?touch=1` / `?touch=0` 的优先级更高，见 TouchInputAvailability）。
        ///
        /// **这是面试现场的救命绳**：自动判据（`Application.isMobilePlatform` 在 iPad 桌面视图等
        /// 场景会失效）判错时，在 Inspector 里拨一下即可，不必重新出包。
        ///
        /// 本字段**每帧重新读取**，因此它同时是 PlayMode 测试进入移动端分支的接缝
        /// （否则 `Application.isMobilePlatform` 在编辑器恒 false，那条分支按构造测不到）。
        /// </summary>
        public TouchOverride TouchOverride = TouchOverride.Auto;

        private InputManager _inputManager;

        /// <summary>
        /// 每帧重新裁决 + 重新发现玩家。**不能一次性 latch**：玩家会被 PlayerSpawner
        /// 销毁重建（每次 `CharacterSelectedEvent`，包括"确认角色"那一下），新玩家带的是全新的
        /// `InputManager`（其 `Awake` 里装的又是 KeyboardInputAdapter）。旧引用被销毁后
        /// `== null` 会重新成立，于是这里会重新发现、重新装适配器 —— 否则手机上确认角色之后
        /// 摇杆与三个按钮会**只画不发**（静默失效，无日志无异常）。
        /// </summary>
        private void Update()
        {
            bool touch = TouchInputAvailability.Resolve(TouchOverride);
            if (_mobileUiRoot != null && _mobileUiRoot.activeSelf != touch)
                _mobileUiRoot.SetActive(touch);
            if (!touch) return;

            if (_inputManager != null) return;   // 已绑定且仍存活（Unity 的 == 对已销毁对象为 true）

            var entry = FindObjectOfType<Sys3CEntry>();
            if (entry == null) return;

            var inputManager = entry.GetComponent<InputManager>();
            if (inputManager == null) return;    // Sys3CEntry.Start 尚未执行

            inputManager.SetAdapter(new JoystickInputAdapter(
                () => _joystick != null ? _joystick.Value : Vector2.zero,
                () => _lookArea != null ? _lookArea.Delta : Vector2.zero));
            _inputManager = inputManager;
        }

        /// <summary>
        /// 帧末汇入动作按钮状态 —— 放在 LateUpdate 是为了与读取方（Sys3CEntry.Update）
        /// 无执行顺序依赖：本帧按下的跳跃必定被下一帧读到且只读到一次。
        /// </summary>
        private void LateUpdate()
        {
            if (_inputManager == null || _buttons == null) return;

            _inputManager.SetAttackHeld(_buttons.AttackHeld);
            _inputManager.SetDefendHeld(_buttons.DefendHeld);
            _inputManager.SetJumpPressed(_buttons.JumpPressed);
            _buttons.ConsumeJump();
        }
    }
}

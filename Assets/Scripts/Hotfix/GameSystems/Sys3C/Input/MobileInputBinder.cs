using UnityEngine;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.UI;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 移动端输入接线 —— 只在触屏平台生效（WebGL 跑在手机上即为触屏）。
    /// 用 Application.isMobilePlatform 判定：PC 浏览器仍走键鼠，不影响既有验收。
    ///
    /// 本组件必须挂在**常驻激活**的对象上：`_mobileUiRoot` 默认不激活，
    /// 若挂在它自身之下，Start 永不执行，手机上 UI 永远不会出现。
    /// </summary>
    public class MobileInputBinder : MonoBehaviour
    {
        [SerializeField] private VirtualJoystick _joystick;
        [SerializeField] private TouchLookArea _lookArea;
        [SerializeField] private MobileActionButtons _buttons;
        [SerializeField] private GameObject _mobileUiRoot;

        private InputManager _inputManager;
        private bool _bound;

        private void Start()
        {
            bool mobile = Application.isMobilePlatform;
            if (_mobileUiRoot != null) _mobileUiRoot.SetActive(mobile);
        }

        /// <summary>
        /// 玩家的 Sys3CEntry 是 PlayerSpawner 在运行期生成的，其 InputManager 又是在
        /// Sys3CEntry.Start 里 AddComponent 的（两个 Start 的顺序未定义），因此引用只能重试获取：
        /// 一次性在 Start 里取会随执行顺序静默拿到 null，触屏输入全失效。
        /// </summary>
        private void Update()
        {
            if (_bound || !Application.isMobilePlatform) return;

            var entry = FindObjectOfType<Sys3CEntry>();
            if (entry == null) return;

            var inputManager = entry.GetComponent<InputManager>();
            if (inputManager == null) return;   // Sys3CEntry.Start 尚未执行

            inputManager.SetAdapter(new JoystickInputAdapter(
                () => _joystick != null ? _joystick.Value : Vector2.zero,
                () => _lookArea != null ? _lookArea.Delta : Vector2.zero));
            _inputManager = inputManager;
            _bound = true;
        }

        /// <summary>
        /// 帧末汇入动作按钮状态 —— 放在 LateUpdate 是为了与读取方（Sys3CEntry.Update）
        /// 无执行顺序依赖：本帧按下的跳跃必定被下一帧读到且只读到一次。
        /// </summary>
        private void LateUpdate()
        {
            if (!_bound || _buttons == null || _inputManager == null) return;

            _inputManager.SetAttackHeld(_buttons.AttackHeld);
            _inputManager.SetDefendHeld(_buttons.DefendHeld);
            _inputManager.SetJumpPressed(_buttons.JumpPressed);
            _buttons.ConsumeJump();
        }
    }
}

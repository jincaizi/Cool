using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.UI;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 触控输入接线 —— 移动端、编辑器与 PC 都显示摇杆/按钮（2026-10-18 产品决定）。
    /// **WASD/鼠标仍可用**：安装 JoystickInputAdapter 时注入 KeyboardInputAdapter 兜底，
    /// 摇杆/观察区空闲时自动回退到键鼠。需要纯键鼠时把 TouchOverride 拨成 ForceNoTouch，
    /// 或用 URL `?touch=0`。
    ///
    /// 本组件必须挂在**常驻激活**的对象上：`_mobileUiRoot` 默认不激活，
    /// 若挂在它自身之下，`Update` 永不执行，UI 永远不会出现。
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
        /// **人工覆盖分两种场合**：编辑器与开发构建里可以拨本字段；
        /// **出包后的 WebGL 产物没有 Inspector —— 那边唯一的入口是 URL `?touch=1` / `?touch=0`**
        /// （面试现场若自动判据失效，靠的就是那个参数）。
        ///
        /// 本字段在运行时被读取（变化即重算），因此它同时是 PlayMode 测试进入移动端分支的接缝
        /// （编辑器现在**默认**就进那条分支，所以这条接缝仍是"显式档位"而非"Auto 的结果"）。
        /// </summary>
        public TouchOverride TouchOverride = TouchOverride.Auto;

        private InputManager _inputManager;
        private TouchOverride _resolvedOverride;
        private bool _touch;
        private bool _resolved;

        /// <summary>
        /// 每帧重新裁决 + 重新发现玩家。**不能一次性 latch**：玩家会被 PlayerSpawner
        /// 销毁重建（每次 `CharacterSelectedEvent`，包括"确认角色"那一下），新玩家带的是全新的
        /// `InputManager`（其 `Awake` 里装的又是 KeyboardInputAdapter）。旧引用被销毁后
        /// `== null` 会重新成立，于是这里会重新发现、重新装适配器 —— 否则手机上确认角色之后
        /// 摇杆与三个按钮会**只画不发**（静默失效，无日志无异常）。
        /// </summary>
        private void Update()
        {
            // 判定不必每帧重算：WebGL 下它要读绝对 URL（每帧一次托管字符串分配）并打一次
            // jslib interop，而这个答案在一次会话内不会自己变 —— 只在人工档位被改时重算。
            if (!_resolved || _resolvedOverride != TouchOverride)
            {
                _touch = TouchInputAvailability.Resolve(TouchOverride);
                _resolvedOverride = TouchOverride;
                _resolved = true;
            }

            if (_mobileUiRoot != null && _mobileUiRoot.activeSelf != _touch)
                _mobileUiRoot.SetActive(_touch);

            if (!_touch)
            {
                // 回退：切到"无触控"必须把适配器还原成键鼠 —— 否则只是把 UI 藏了，
                // Joystick 适配器还挂在玩家身上，而 LateUpdate 也还在喂它。
                if (_inputManager != null)
                {
                    _inputManager.SetAdapter(new KeyboardInputAdapter());
                    _inputManager = null;
                }
                return;
            }

            if (_inputManager != null) return;   // 已绑定且仍存活（Unity 的 == 对已销毁对象为 true）

            var entry = PickLocalEntry(FindObjectsOfType<Sys3CEntry>());
            if (entry == null) return;

            var inputManager = entry.GetComponent<InputManager>();
            if (inputManager == null) return;    // Sys3CEntry.Start 尚未执行

            inputManager.SetAdapter(new JoystickInputAdapter(
                () => _joystick != null ? _joystick.Value : Vector2.zero,
                () => _lookArea != null ? _lookArea.Delta : Vector2.zero,
                new KeyboardInputAdapter()));
            _inputManager = inputManager;
        }

        /// <summary>
        /// 从候选里挑出**本地**的 `Sys3CEntry`；全是远端视图（或没有候选）时返回 null。
        ///
        /// 为什么不能直接用 `FindObjectOfType<Sys3CEntry>()` 的单个返回值：它**会返回启用
        /// GameObject 上的禁用组件**（2026-10-13 实测，与 `ClientNetworkHost.FindLocalEntry`
        /// 同一类洞），而远端头像上正是"预制体自带的 `Sys3CEntry` 被
        /// `RemoteEntityAnimator.Awake` 置 enabled=false"（不是移除）。若在"远端视图已建、
        /// 本地角色尚未装配"的重发现窗口（玩家被 PlayerSpawner 销毁重建）里选中它，
        /// 摇杆适配器会装到**别人身上**——手机上摇杆与三个动作按钮完全没反应，且无任何日志。
        ///
        /// 判据基于**组件**而非名字（`RemotePlayers` 根可改名）：候选自身或父链上存在
        /// `RemoteEntityAnimator`（`RemoteEntityRegistry` 给每个玩家类远程视图挂的驱动）
        /// 即判为远端。查父链要 `includeInactive`：Proxy 档会把视图整棵 `SetActive(false)`。
        /// 独立成 internal static 纯函数供 EditMode 测试直接调
        /// （与 `NpcBodyCollider.BuildCollider` / `NpcEntityAnimator.ResolveAnimator` 同惯例）。
        /// </summary>
        internal static Sys3CEntry PickLocalEntry(IReadOnlyList<Sys3CEntry> candidates)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null) continue;
                // 远端候选：排除（含驱动与 entry 挂在同一个 GameObject 上的真机布局）
                if (candidate.GetComponentInParent<RemoteEntityAnimator>(true) != null) continue;
                return candidate;
            }
            return null;
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

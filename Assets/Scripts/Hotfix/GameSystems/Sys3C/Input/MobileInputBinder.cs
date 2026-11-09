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
        [SerializeField] private MobileSkillAimJoystick _fireballAim;
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
        private bool _gateBlocked;
        private Sys3CEntry _boundEntry;

        public Sys3CEntry BoundEntry => _boundEntry;

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

            // 阻断期间释放绑定并隐藏触控 UI：按钮/摇杆的 OnDisable 会清掉 held 状态，
            // 遮罩解除后必须重新按下才会产生新的输入。
            bool blocked = InputBlockGate.IsBlocked;
            if (blocked != _gateBlocked)
            {
                _gateBlocked = blocked;
                if (blocked) ReleaseBinding();
            }

            bool showMobileUi = _touch && !blocked;
            if (_mobileUiRoot != null && _mobileUiRoot.activeSelf != showMobileUi)
                _mobileUiRoot.SetActive(showMobileUi);

            if (blocked) return;

            if (!_touch)
            {
                // 回退：切到"无触控"必须把适配器还原成键鼠 —— 否则只是把 UI 藏了，
                // Joystick 适配器还挂在玩家身上，而 LateUpdate 也还在喂它。
                // 捕获旧引用后再释放绑定：ReleaseBinding 会清掉触摸状态并把两者置空。
                var previousInput = _inputManager;
                ReleaseBinding();
                if (previousInput != null) previousInput.SetAdapter(new KeyboardInputAdapter());
                return;
            }

            // 已绑定且两个引用都存活（Unity 的 == 对已销毁对象为 true）。
            // 只检查其中一个是缺陷：OnDisable 曾只清 _boundEntry 而留下 _inputManager，
            // 再启用后会永久早退，HUD 永远拿不到 BoundEntry。
            if (_inputManager != null && _boundEntry != null) return;

            // 角色销毁重建 / 绑定切换：必须先把旧按钮状态与旧 InputManager 清干净，
            // 否则重建时仍按住的普攻/火球/炎爆会被 LateUpdate 直接写进新角色。
            ReleaseBinding();

            var entry = PickLocalEntry(FindObjectsOfType<Sys3CEntry>());
            if (entry == null) return;

            var inputManager = entry.GetComponent<InputManager>();
            if (inputManager == null) return;    // Sys3CEntry.Start 尚未执行

            inputManager.SetAdapter(new JoystickInputAdapter(
                () => _joystick != null ? _joystick.Value : Vector2.zero,
                () => _lookArea != null ? _lookArea.Delta : Vector2.zero,
                new KeyboardInputAdapter()));
            _inputManager = inputManager;
            _boundEntry = entry;
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
        /// 冲刺标志同样在帧末汇入，下一帧生效；它不依赖 `_buttons`（摇杆未配置按钮时也照喂）。
        /// </summary>
        private void LateUpdate()
        {
            // 阻断期间不得把按钮旧状态重新写回 InputManager；恢复后等待新的按下。
            if (InputBlockGate.IsBlocked) return;
            if (_inputManager == null) return;

            // 摇杆手势不依赖 MobileActionButtons：_buttons 未配置时也要喂。
            _inputManager.SetSprintHeld(_joystick != null && _joystick.IsSprintHeld);

            if (_buttons == null) return;

            _inputManager.SetAttackHeld(_buttons.AttackHeld);
            _inputManager.SetDefendHeld(_buttons.DefendHeld);
            _inputManager.SetJumpPressed(_buttons.JumpPressed);
            _buttons.ConsumeJump();

            // 一次性按下必须**无条件**写入（包括 false）。Unity 保证本帧所有 Update
            // 先于 LateUpdate，消费者有完整的下一帧 Update 窗口读取 pressed；若死亡/
            // 防御分支整块跳过读取，下一帧这里的 false 会把未消费的锁存丢弃，避免
            // 解除防御或复活后补放一次玩家当时没有请求的技能。
            _inputManager.SetSkillQPressedFromUI(_buttons.SkillQPressed);
            _inputManager.SetSkillRPressedFromUI(_buttons.SkillRPressed);
            _inputManager.SetSkillFireballPressedFromUI(_buttons.FireballPressed);
            _inputManager.SetSkillChargeBlastPressedFromUI(_buttons.ChargeBlastPressed);
            _inputManager.SetSkillMeteorPressedFromUI(_buttons.MeteorPressed);
            _inputManager.SetSkillFireballHeldFromUI(_buttons.FireballHeld);
            _inputManager.SetSkillChargeBlastHeldFromUI(_buttons.ChargeBlastHeld);
            _buttons.ConsumeSkillPresses();

            if (_buttons.TargetSwitchPressed)
            {
                _inputManager.SetTargetSwitchPressedFromUI(true);
                _buttons.ConsumeTargetSwitch();
            }

            // 瞄准状态必须最后写：`SetSkillFireballPressedFromUI(true)` 会清掉旧的释放
            // 锁存，同一帧「按下即松开」时若先写 release 就会被 pressed 清掉，
            // 下一帧 level-based 释放会把技能打出去。
            if (_fireballAim != null)
            {
                _inputManager.SetSkillFireballAimFromUI(
                    _fireballAim.IsGestureActive, _fireballAim.Aim, _fireballAim.CancelArmed);
                var kind = _fireballAim.ConsumeReleaseKind();
                if (kind != AimReleaseKind.None)
                    _inputManager.SetSkillFireballReleaseFromUI(kind == AimReleaseKind.Cancel);
            }
            else
            {
                _inputManager.SetSkillFireballAimFromUI(false, Vector2.zero, false);
            }
        }

        /// <summary>
        /// 停用/失焦时清掉 UI 冲刺与全部触摸状态，并**同时**释放两个绑定引用。
        /// 只清 `_boundEntry` 会让再启用后的 `Update` 因 `_inputManager != null` 永久早退，
        /// HUD 再也拿不到 `BoundEntry`（技能栏会一直复位）。
        /// </summary>
        private void OnDisable()
        {
            ReleaseBinding();
            // 只清自己持有的原因；Landscape 由常驻方向门持有。
            InputBlockGate.Set(InputBlockGate.Reason.FocusLost, false);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // 浏览器失焦是"多原因阻断"的一支：竖屏遮罩还在时，单独切回前台不得解除阻断。
            // 仅 WebGL 构建启用 —— Editor/PlayMode 测试的窗口焦点不代表游戏输入状态。
            if (hasFocus)
            {
                InputBlockGate.Set(InputBlockGate.Reason.FocusLost, false);
                return;
            }

            // 只有当前真的持有输入时才阻断。
            if (_inputManager == null && _boundEntry == null) return;

            InputBlockGate.Set(InputBlockGate.Reason.FocusLost, true);
#else
            if (hasFocus) return;
#endif
            // 顺序：先取消正在瞄准的技能，再清 UI 状态 —— 否则清掉的释放锁存
            // 会让蓄力中的火球在失焦后被 level-based 释放打出去。
            if (_boundEntry != null) _boundEntry.CancelAimingSkill();
            if (_fireballAim != null) _fireballAim.ResetGesture();
            if (_buttons != null) _buttons.ClearCombatInput();
            ClearCombatUiInput(_inputManager);
            if (_boundEntry != null) _boundEntry.ClearHeavyReady();
        }

        /// <summary>
        /// 释放当前绑定：清掉 UI 与旧 InputManager 的 held/pressed，并把两个引用一起置空。
        /// 角色销毁重建、切换绑定、关闭触控与 Binder 停用都走这里，保证状态不会漏给新角色。
        /// </summary>
        private void ReleaseBinding()
        {
            // 先取消瞄准（不发射、不冷却），再清 UI 状态。
            if (_boundEntry != null) _boundEntry.CancelAimingSkill();
            if (_fireballAim != null) _fireballAim.ResetGesture();
            if (_boundEntry != null) _boundEntry.ClearHeavyReady();
            if (_buttons != null) _buttons.ClearCombatInput();
            ClearCombatUiInput(_inputManager);
            _inputManager = null;
            _boundEntry = null;
        }

        internal static void ClearCombatUiInput(InputManager inputManager)
        {
            if (inputManager == null) return;
            inputManager.SetSprintHeld(false);
            inputManager.SetAttackHeld(false);
            inputManager.CancelAttackHold();
            inputManager.SetDefendHeld(false);
            inputManager.SetJumpPressed(false);
            inputManager.SetSkillQPressedFromUI(false);
            inputManager.SetSkillRPressedFromUI(false);
            inputManager.SetSkillFireballPressedFromUI(false);
            inputManager.SetSkillFireballHeldFromUI(false);
            inputManager.SetSkillFireballAimFromUI(false, Vector2.zero, false);
            inputManager.ClearSkillFireballReleaseFromUI();
            inputManager.SetSkillChargeBlastPressedFromUI(false);
            inputManager.SetSkillChargeBlastHeldFromUI(false);
            inputManager.SetSkillMeteorPressedFromUI(false);
        }
    }
}

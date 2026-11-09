using Hotfix.GameSystems.Sys3C.Character;
using UnityEngine;
using UnityInput = UnityEngine.Input;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 输入管理器 — 统一所有输入适配器，输出标准化 MoveCommand
    /// 同时提供跳跃/攻击/冲刺的即时输入检测
    /// </summary>
    public class InputManager : MonoBehaviour
    {
        private IInputAdapter _adapter;

        // 阻断解除后，仍按住的源必须先释放，才接受下一次按下。
        // 普攻不在其中：鼠标左键已全局取消，按钮状态在阻断时由 Binder 清除。
        private bool _moveNeedsRelease;
        private bool _lookNeedsRelease;
        private bool _sprintNeedsRelease;
        private bool _defendNeedsRelease;
        private bool _fireballNeedsRelease;
        private bool _chargeBlastNeedsRelease;

        // 移动速度
        public float MoveSpeed { get; set; } = 5.0f;
        public float SprintSpeed { get; set; } = 9.0f;

        /// <summary>力竭门禁：false 时 GetMoveCommand 不会产出冲刺。</summary>
        public bool SprintAllowed { get; set; } = true;

        private bool _sprintHeldFromUI;
        private bool _sprintHeldFromKeyboard;

        // 力竭锁存：SprintAllowed 变 false 时若仍按住冲刺，必须释放一次才能再次冲刺。
        private bool _sprintBlockedUntilRelease;

        /// <summary>由移动端摇杆手势 / UI 按键调用（手机端由 MobileInputBinder 每帧汇入）。</summary>
        public void SetSprintHeld(bool held)
        {
            if (held && InputBlockGate.IsBlocked) return;
            _sprintHeldFromUI = held;
        }

        /// <summary>复活等“满精力重置”场景清掉力竭锁存。</summary>
        public void ResetSprintLatch()
        {
            _sprintBlockedUntilRelease = false;
        }

        // 相机旋转灵敏度
        public float CameraSensitivityX { get; set; } = 2.0f;
        public float CameraSensitivityY { get; set; } = 2.0f;

        // === 一次性事件（每帧只触发一次） ===
        private bool _jumpConsumed;
        private bool _skill2Consumed;
        private bool _skill3Consumed;
        private bool _skillFireballConsumed;
        private bool _skillChargeBlastConsumed;
        private bool _skillMeteorConsumed;
        private bool _skillQPressedFromUI;
        private bool _skillRPressedFromUI;
        private bool _skillFireballPressedFromUI;
        private bool _skillFireballHeldFromUI;
        private bool _skillChargeBlastPressedFromUI;
        private bool _skillChargeBlastHeldFromUI;
        private bool _skillMeteorPressedFromUI;

        // 火球地面瞄准（移动端摇杆）—— 由 MobileInputBinder 帧末汇入。
        private bool _skillFireballAimActiveFromUI;
        private Vector2 _skillFireballAimFromUI;
        private bool _skillFireballCancelArmedFromUI;
        private bool _skillFireballReleaseFromUI;
        private bool _skillFireballReleaseCanceledFromUI;
        private bool _skillFireballReleaseConsumed = true;

        // 切换目标一次性事件（T 键 / 移动端按钮）—— UI 标志只在消费时清除，
        // Update 只重置消费锁存，避免 LateUpdate 汇入的按下被下一帧 Update 提前丢掉。
        private bool _targetSwitchPressedFromUI;
        private bool _targetSwitchConsumed;

        // Attack button hold tracking
        private bool _attackHeld;
        private float _attackHoldStart = -1f;
        private float _lastReleaseDuration = -1f;
        private bool _attackJustPressed;

        private void Awake()
        {
            _adapter = new KeyboardInputAdapter();
        }

        private void OnEnable()
        {
            InputBlockGate.Changed += OnInputBlockChanged;
            if (InputBlockGate.IsBlocked) SuspendForBlock();
        }

        private void OnDisable()
        {
            InputBlockGate.Changed -= OnInputBlockChanged;
        }

        private void OnInputBlockChanged()
        {
            if (InputBlockGate.IsBlocked) SuspendForBlock();
        }

        /// <summary>
        /// 阻断生效时立即取消所有未消费的手势。不合成释放（不产生重击），
        /// 也不触碰已经提交给技能协调器的动作。
        /// </summary>
        private void SuspendForBlock()
        {
            _sprintHeldFromKeyboard = false;
            _sprintHeldFromUI = false;
            _attackHeldFromUI = false;
            _defendHeldFromUI = false;
            _skillFireballHeldFromUI = false;
            _skillChargeBlastHeldFromUI = false;
            _jumpPressedFromUI = false;
            _skillQPressedFromUI = false;
            _skillRPressedFromUI = false;
            _skillFireballPressedFromUI = false;
            _skillChargeBlastPressedFromUI = false;
            _skillMeteorPressedFromUI = false;
            _targetSwitchPressedFromUI = false;
            _skillFireballAimActiveFromUI = false;
            _skillFireballAimFromUI = Vector2.zero;
            _skillFireballCancelArmedFromUI = false;
            _skillFireballReleaseFromUI = false;
            _skillFireballReleaseCanceledFromUI = false;
            _skillFireballReleaseConsumed = true;

            CancelAttackHold();

            _moveNeedsRelease = true;
            _lookNeedsRelease = true;
            _sprintNeedsRelease = true;
            _defendNeedsRelease = true;
            _fireballNeedsRelease = true;
            _chargeBlastNeedsRelease = true;
        }

        /// <summary>注入适配器 —— 触控 UI 显示时由 MobileInputBinder 安装 Joystick（含 KeyboardInputAdapter 兜底）；
        /// 不调用则维持键鼠。</summary>
        public void SetAdapter(IInputAdapter adapter)
        {
            if (adapter != null) _adapter = adapter;
        }

        /// <summary>
        /// 每帧更新 — 重置即时事件状态
        /// </summary>
        public void Update()
        {
            _jumpConsumed = false;
            _skill2Consumed = false;
            _skill3Consumed = false;
            _skillFireballConsumed = false;
            _skillChargeBlastConsumed = false;
            _skillMeteorConsumed = false;
            _skillFireballReleaseConsumed = false;
            _targetSwitchConsumed = false;
            _lastReleaseDuration = -1f;
            _attackJustPressed = false;
            _sprintHeldFromKeyboard = false;

            // 门控先于采样：阻断期间不读取按钮输入，也不留下可被下一帧消费的旧状态。
            if (InputBlockGate.IsBlocked)
            {
                SuspendForBlock();
                return;
            }

            _sprintHeldFromKeyboard = UnityInput.GetKey(KeyCode.LeftShift);

            // 普攻只认触控 HUD 的普攻按钮（按下 / 按住 / 松开），所有平台一致。
            // 鼠标左键不再采集成攻击信号，场景与 UI 上的点击都不挥刀。
            bool attackDown = _attackHeldFromUI;
            if (attackDown && !_attackHeld)
            {
                _attackHeld = true;
                _attackHoldStart = Time.time;
                _attackJustPressed = true;
            }
            else if (!attackDown && _attackHeld)
            {
                _lastReleaseDuration = Time.time - _attackHoldStart;
                _attackHeld = false;
                _attackHoldStart = -1f;
            }
        }

        /// <summary>
        /// 获取标准化移动命令
        /// </summary>
        public MoveCommand GetMoveCommand(Vector3 cameraForward)
        {
            if (InputBlockGate.IsBlocked) return ZeroMove();

            Vector3 moveInput = _adapter.GetMoveInput();

            // 解除阻断时仍按住的移动键/摇杆必须先归零，避免角色恢复后继续跑。
            if (_moveNeedsRelease)
            {
                if (moveInput.sqrMagnitude > 0.01f) return ZeroMove();
                _moveNeedsRelease = false;
            }

            bool rawSprintHeld = IsSprintHeld();

            if (!SprintAllowed && rawSprintHeld)
                _sprintBlockedUntilRelease = true;
            else if (!rawSprintHeld)
                _sprintBlockedUntilRelease = false;

            bool sprintHeld = rawSprintHeld && SprintAllowed && !_sprintBlockedUntilRelease;
            float speed = sprintHeld ? SprintSpeed : MoveSpeed;

            if (moveInput.sqrMagnitude > 0.01f)
            {
                // 将输入从相机视角转换为世界方向
                Vector3 worldMoveDir = ConvertToWorldDirection(moveInput, cameraForward);

                // 冲刺是二元状态：忽略推杆深度。真实速度由 CharacterController.SprintSpeed（8.0f）决定；
                // 不归一化的话，半推冲刺 4 m/s 会比满推走路 5 m/s 还慢。
                if (sprintHeld) worldMoveDir = worldMoveDir.normalized;

                Quaternion targetRotation = Quaternion.LookRotation(worldMoveDir);

                return new MoveCommand
                {
                    MoveDir = worldMoveDir,
                    Speed = speed,
                    Rotation = targetRotation,
                    IsSprint = sprintHeld
                };
            }

            return new MoveCommand
            {
                MoveDir = Vector3.zero,
                Speed = 0f,
                Rotation = Quaternion.identity,
                IsSprint = false
            };
        }

        /// <summary>本次视角采样是否来自触屏；阻断 / 释放门控期间清 false。</summary>
        public bool IsLookFromTouch { get; private set; }

        /// <summary>
        /// 获取相机旋转输入（原始向量）
        /// </summary>
        public Vector2 GetCameraRotationInput()
        {
            if (InputBlockGate.IsBlocked)
            {
                IsLookFromTouch = false;
                return Vector2.zero;
            }

            Vector2 look = _adapter.GetCameraRotationInput();
            IsLookFromTouch = _adapter is JoystickInputAdapter joystick
                && joystick.LastLookFromTouch;

            if (_lookNeedsRelease)
            {
                IsLookFromTouch = false;
                if (look.sqrMagnitude > 0f) return Vector2.zero;
                _lookNeedsRelease = false;
            }

            return look;
        }

        /// <summary>
        /// 是否正在移动
        /// </summary>
        public bool IsMoving()
        {
            if (InputBlockGate.IsBlocked) return false;
            if (_moveNeedsRelease) return false;
            return _adapter.HasMoveInput();
        }

        /// <summary>
        /// 跳跃按下（一次性事件，按住空格只触发一次跳跃）
        /// </summary>
        public bool IsJumpPressed()
        {
            if (InputBlockGate.IsBlocked) return false;
            bool pressed = UnityInput.GetButtonDown("Jump") || _jumpPressedFromUI;
            if (pressed && !_jumpConsumed)
            {
                _jumpConsumed = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 切换目标按下（一次性事件）— T 键或移动端 UI 按钮。消费一次即返回 false；
        /// 键盘边沿由 `Update` 每帧重置的去重锁存保证不重复。
        /// </summary>
        public bool IsTargetSwitchPressed()
        {
            if (InputBlockGate.IsBlocked) return false;
            bool pressed = _targetSwitchPressedFromUI || UnityInput.GetKeyDown(KeyCode.T);
            if (!pressed || _targetSwitchConsumed) return false;
            _targetSwitchConsumed = true;
            _targetSwitchPressedFromUI = false;
            return true;
        }

        /// <summary>由移动端 UI 按钮调用，置位切换目标的一次性按下（消费后自动清除）。</summary>
        public void SetTargetSwitchPressedFromUI(bool pressed)
        {
            if (pressed && InputBlockGate.IsBlocked) return;
            if (pressed)
            {
                _targetSwitchPressedFromUI = true;
                _targetSwitchConsumed = false;
            }
        }

        /// <summary>
        /// 攻击键刚松开时返回按住时长（秒）。未松开返回 -1。
        /// </summary>
        /// <summary>
        /// 攻击键刚按下的那一帧返回 true
        /// </summary>
        public bool IsAttackJustPressed()
        {
            return !InputBlockGate.IsBlocked && _attackJustPressed;
        }

        public float GetAttackReleaseDuration()
        {
            return InputBlockGate.IsBlocked ? -1f : _lastReleaseDuration;
        }

        /// <summary>
        /// 攻击键是否按住超过指定秒数
        /// </summary>
        public bool IsAttackHeldOver(float seconds)
        {
            if (InputBlockGate.IsBlocked) return false;
            return _attackHeld && _attackHoldStart > 0f && (Time.time - _attackHoldStart) >= seconds;
        }

        /// <summary>
        /// 攻击键是否正在按住
        /// </summary>
        public bool IsAttackHeld()
        {
            return !InputBlockGate.IsBlocked && _attackHeld;
        }

        /// <summary>
        /// 冲刺按住（持续状态）
        /// </summary>
        public bool IsSprintHeld()
        {
            if (InputBlockGate.IsBlocked) return false;

            bool raw = _sprintHeldFromUI || _sprintHeldFromKeyboard;
            if (_sprintNeedsRelease)
            {
                if (raw) return false;
                _sprintNeedsRelease = false;
            }
            return raw;
        }

        // UI 按钮设置的防御状态（手游平台使用）
        private bool _defendHeldFromUI;

        /// <summary>
        /// 由 UI 按钮调用，设置防御状态。手游平台通过按钮的按下/释放事件调用。
        /// </summary>
        public void SetDefendHeld(bool held)
        {
            if (held && InputBlockGate.IsBlocked) return;
            _defendHeldFromUI = held;
        }

        // UI 按钮设置的攻击按住状态（手游平台使用）—— 与鼠标在 Update 里合并成同一条边沿
        private bool _attackHeldFromUI;

        /// <summary>由 UI 按钮调用，设置攻击按住状态（与 SetDefendHeld 同形）。</summary>
        public void SetAttackHeld(bool held)
        {
            if (held && InputBlockGate.IsBlocked) return;
            _attackHeldFromUI = held;
        }

        /// <summary>
        /// 无释放事件地取消攻击按住追踪（失焦、关闭触控、释放绑定）。
        /// 与 `SetAttackHeld(false)` 不同：不会让 `Update` 合成一次释放时长，
        /// 否则 Sys3CEntry 的“松开 = 重击”分支会在浏览器失焦后补放一次重击。
        /// </summary>
        public void CancelAttackHold()
        {
            _attackHeld = false;
            _attackHoldStart = -1f;
            _lastReleaseDuration = -1f;
            _attackJustPressed = false;
        }

        // UI 按钮设置的跳跃按下锁存（手游平台使用）—— 消费掉一次性按下是 UI 侧的责任
        private bool _jumpPressedFromUI;

        /// <summary>由 UI 按钮调用，置位跳跃的一次性按下（手游平台使用）。</summary>
        public void SetJumpPressed(bool pressed)
        {
            if (pressed && InputBlockGate.IsBlocked) return;
            _jumpPressedFromUI = pressed;
        }

        public void SetSkillQPressedFromUI(bool pressed)
        {
            if (pressed && InputBlockGate.IsBlocked) return;
            _skillQPressedFromUI = pressed;
            if (pressed) _skill2Consumed = false;
        }

        public void SetSkillRPressedFromUI(bool pressed)
        {
            if (pressed && InputBlockGate.IsBlocked) return;
            _skillRPressedFromUI = pressed;
            if (pressed) _skill3Consumed = false;
        }

        public void SetSkillFireballPressedFromUI(bool pressed)
        {
            if (pressed && InputBlockGate.IsBlocked) return;
            _skillFireballPressedFromUI = pressed;
            if (pressed)
            {
                _skillFireballConsumed = false;
                // 新的按下属于新一次技能输入：旧释放锁存不得再被消费。
                _skillFireballReleaseFromUI = false;
                _skillFireballReleaseConsumed = true;
            }
        }

        public void SetSkillFireballHeldFromUI(bool held)
        {
            if (held && InputBlockGate.IsBlocked) return;
            _skillFireballHeldFromUI = held;
        }

        public void SetSkillChargeBlastPressedFromUI(bool pressed)
        {
            if (pressed && InputBlockGate.IsBlocked) return;
            _skillChargeBlastPressedFromUI = pressed;
            if (pressed) _skillChargeBlastConsumed = false;
        }

        public void SetSkillChargeBlastHeldFromUI(bool held)
        {
            if (held && InputBlockGate.IsBlocked) return;
            _skillChargeBlastHeldFromUI = held;
        }

        public void SetSkillMeteorPressedFromUI(bool pressed)
        {
            if (pressed && InputBlockGate.IsBlocked) return;
            _skillMeteorPressedFromUI = pressed;
            if (pressed) _skillMeteorConsumed = false;
        }

        /// <summary>
        /// 防御键按住（持续状态）— PC/Editor 键盘 F，或手游 UI 按钮
        /// </summary>
        public bool IsDefendHeld()
        {
            if (InputBlockGate.IsBlocked) return false;

            bool raw = _defendHeldFromUI || UnityInput.GetKey(KeyCode.F);
            if (_defendNeedsRelease)
            {
                if (raw) return false;
                _defendNeedsRelease = false;
            }
            return raw;
        }

        /// <summary>
        /// 2技能按下（Q键）- 普通攻击升级版
        /// </summary>
        public bool IsSkill2Pressed()
        {
            if (InputBlockGate.IsBlocked) return false;
            bool pressed = _skillQPressedFromUI || UnityInput.GetKeyDown(KeyCode.Q);
            if (pressed && !_skill2Consumed)
            {
                _skill2Consumed = true;
                _skillQPressedFromUI = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 3技能按下（R键）- 大招
        /// </summary>
        public bool IsSkill3Pressed()
        {
            if (InputBlockGate.IsBlocked) return false;
            bool pressed = _skillRPressedFromUI || UnityInput.GetKeyDown(KeyCode.R);
            if (pressed && !_skill3Consumed)
            {
                _skill3Consumed = true;
                _skillRPressedFromUI = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 火球术按下（1键）
        /// </summary>
        public bool IsSkillFireballPressed()
        {
            if (InputBlockGate.IsBlocked) return false;
            bool pressed = _skillFireballPressedFromUI || UnityInput.GetKeyDown(KeyCode.Alpha1);
            if (pressed && !_skillFireballConsumed)
            {
                _skillFireballConsumed = true;
                _skillFireballPressedFromUI = false;
                return true;
            }
            return false;
        }

        /// <summary>只读 UI 按下沿（不清锁存）。激活时用来区分移动端手势与 PC 键鼠；
        /// 必须在 <see cref="IsSkillFireballPressed"/> 之前读取——后者消费时会清零该标志。</summary>
        public bool IsSkillFireballPressedFromUI()
            => !InputBlockGate.IsBlocked && _skillFireballPressedFromUI;

        /// <summary>
        /// 火球术按住（1键，持续状态）— 蓄力瞄准
        /// </summary>
        public bool IsSkillFireballHeld()
        {
            if (InputBlockGate.IsBlocked) return false;

            bool raw = _skillFireballHeldFromUI || UnityInput.GetKey(KeyCode.Alpha1);
            if (_fireballNeedsRelease)
            {
                if (raw) return false;
                _fireballNeedsRelease = false;
            }
            return raw;
        }

        /// <summary>
        /// 火球地面瞄准状态（移动端摇杆）—— 由 MobileInputBinder 在帧末汇入。
        /// 非瞄准时 aim 归零、取消态清零。
        /// </summary>
        public void SetSkillFireballAimFromUI(bool active, Vector2 aim, bool cancelArmed)
        {
            if (active && InputBlockGate.IsBlocked) return;
            _skillFireballAimActiveFromUI = active;
            _skillFireballAimFromUI = active ? aim : Vector2.zero;
            _skillFireballCancelArmedFromUI = active && cancelArmed;
        }

        public bool IsSkillFireballAimActive()
            => !InputBlockGate.IsBlocked && _skillFireballAimActiveFromUI;
        public Vector2 SkillFireballAim
            => InputBlockGate.IsBlocked ? Vector2.zero : _skillFireballAimFromUI;
        public bool IsSkillFireballCancelArmed()
            => !InputBlockGate.IsBlocked && _skillFireballCancelArmedFromUI;

        /// <summary>瞄准摇杆释放：置位一次释放锁存（一次按下最多消费一次）。</summary>
        public void SetSkillFireballReleaseFromUI(bool canceled)
        {
            if (InputBlockGate.IsBlocked) return;
            _skillFireballReleaseFromUI = true;
            _skillFireballReleaseCanceledFromUI = canceled;
            _skillFireballReleaseConsumed = false;
        }

        /// <summary>消费释放锁存；未置位或已消费返回 false。消费后清锁存：
        /// `Update` 每帧复位 `_skillFireballReleaseConsumed`，不清 `FromUI` 会让旧释放反复可消费。</summary>
        public bool ConsumeSkillFireballRelease(out bool canceled)
        {
            canceled = _skillFireballReleaseCanceledFromUI;
            if (InputBlockGate.IsBlocked) return false;
            if (!_skillFireballReleaseFromUI || _skillFireballReleaseConsumed) return false;
            _skillFireballReleaseConsumed = true;
            _skillFireballReleaseFromUI = false;
            return true;
        }

        /// <summary>是否还有未消费的 UI 释放锁存。移动端松手在帧末写入、下一帧才被消费，
        /// 期间 aim 状态已归零——Sys3CEntry 据此跳过 PC 兜底，保住拖动落点。</summary>
        public bool HasSkillFireballReleasePending()
            => !InputBlockGate.IsBlocked
               && _skillFireballReleaseFromUI
               && !_skillFireballReleaseConsumed;

        /// <summary>teardown 清掉未消费的释放锁存（避免复活后补取消）。</summary>
        public void ClearSkillFireballReleaseFromUI()
        {
            _skillFireballReleaseFromUI = false;
            _skillFireballReleaseCanceledFromUI = false;
            _skillFireballReleaseConsumed = true;
        }

        /// <summary>
        /// 蓄力炎爆按下（2键）— 按住蓄力
        /// </summary>
        public bool IsSkillChargeBlastPressed()
        {
            if (InputBlockGate.IsBlocked) return false;
            bool pressed = _skillChargeBlastPressedFromUI || UnityInput.GetKeyDown(KeyCode.Alpha2);
            if (pressed && !_skillChargeBlastConsumed)
            {
                _skillChargeBlastConsumed = true;
                _skillChargeBlastPressedFromUI = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 蓄力炎爆按住（2键，持续状态）
        /// </summary>
        public bool IsSkillChargeBlastHeld()
        {
            if (InputBlockGate.IsBlocked) return false;

            bool raw = _skillChargeBlastHeldFromUI || UnityInput.GetKey(KeyCode.Alpha2);
            if (_chargeBlastNeedsRelease)
            {
                if (raw) return false;
                _chargeBlastNeedsRelease = false;
            }
            return raw;
        }

        /// <summary>
        /// 陨石术按下（3键）- 大招
        /// </summary>
        public bool IsSkillMeteorPressed()
        {
            if (InputBlockGate.IsBlocked) return false;
            bool pressed = _skillMeteorPressedFromUI || UnityInput.GetKeyDown(KeyCode.Alpha3);
            if (pressed && !_skillMeteorConsumed)
            {
                _skillMeteorConsumed = true;
                _skillMeteorPressedFromUI = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 是否“按住冲刺且正在移动”。**不含 SprintAllowed / 力竭锁存**，当前无消费者。
        /// 将来如需判断“实际是否在冲刺”，必须读 `GetMoveCommand().IsSprint`，
        /// 否则力竭/锁存期间会误报。
        /// </summary>
        public bool IsSprinting()
        {
            return IsSprintHeld() && IsMoving();
        }

        private static MoveCommand ZeroMove()
        {
            return new MoveCommand
            {
                MoveDir = Vector3.zero,
                Speed = 0f,
                Rotation = Quaternion.identity,
                IsSprint = false
            };
        }

        /// <summary>
        /// 将输入向量从相机视角转换为世界方向
        /// </summary>
        private Vector3 ConvertToWorldDirection(Vector3 input, Vector3 cameraForward)
        {
            // 以相机朝向为基准计算世界方向
            Quaternion cameraRotation = Quaternion.LookRotation(cameraForward);
            return cameraRotation * input;
        }
    }
}

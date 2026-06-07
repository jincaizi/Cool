using Hotfix.GameSystems.Sys3C.Character;
using System;
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

        // 移动速度
        public float MoveSpeed { get; set; } = 5.0f;
        public float SprintSpeed { get; set; } = 9.0f;

        // 相机旋转灵敏度
        public float CameraSensitivityX { get; set; } = 2.0f;
        public float CameraSensitivityY { get; set; } = 2.0f;

        // === 一次性事件（每帧只触发一次） ===
        private bool _jumpConsumed;
        private bool _skill2Consumed;
        private bool _skill3Consumed;

        // Attack button hold tracking
        private bool _attackHeld;
        private float _attackHoldStart = -1f;
        private float _lastReleaseDuration = -1f;
        private bool _attackJustPressed;

        private void Awake()
        {
            _adapter = new KeyboardInputAdapter();
        }

        /// <summary>
        /// 每帧更新 — 重置即时事件状态
        /// </summary>
        public void Update()
        {
            _jumpConsumed = false;
            _skill2Consumed = false;
            _skill3Consumed = false;
            _lastReleaseDuration = -1f;
            _attackJustPressed = false;

            bool mouseDown = UnityInput.GetMouseButton(0);
            if (mouseDown && !_attackHeld)
            {
                _attackHeld = true;
                _attackHoldStart = Time.time;
                _attackJustPressed = true;
            }
            else if (!mouseDown && _attackHeld)
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
            Vector3 moveInput = _adapter.GetMoveInput();

            bool sprintHeld = IsSprintHeld();
            float speed = sprintHeld ? SprintSpeed : MoveSpeed;

            if (moveInput.sqrMagnitude > 0.01f)
            {
                // 将输入从相机视角转换为世界方向
                Vector3 worldMoveDir = ConvertToWorldDirection(moveInput, cameraForward);
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

        /// <summary>
        /// 获取相机旋转输入（原始向量）
        /// </summary>
        public Vector2 GetCameraRotationInput()
        {
            return _adapter.GetCameraRotationInput();
        }

        /// <summary>
        /// 是否正在移动
        /// </summary>
        public bool IsMoving()
        {
            return _adapter.HasMoveInput();
        }

        /// <summary>
        /// 跳跃按下（一次性事件，按住空格只触发一次跳跃）
        /// </summary>
        public bool IsJumpPressed()
        {
            bool pressed = UnityInput.GetButtonDown("Jump");
            if (pressed && !_jumpConsumed)
            {
                _jumpConsumed = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 攻击键刚松开时返回按住时长（秒）。未松开返回 -1。
        /// </summary>
        /// <summary>
        /// 攻击键刚按下的那一帧返回 true
        /// </summary>
        public bool IsAttackJustPressed()
        {
            return _attackJustPressed;
        }

        public float GetAttackReleaseDuration()
        {
            return _lastReleaseDuration;
        }

        /// <summary>
        /// 攻击键是否按住超过指定秒数
        /// </summary>
        public bool IsAttackHeldOver(float seconds)
        {
            return _attackHeld && _attackHoldStart > 0f && (Time.time - _attackHoldStart) >= seconds;
        }

        /// <summary>
        /// 攻击键是否正在按住
        /// </summary>
        public bool IsAttackHeld()
        {
            return _attackHeld;
        }

        /// <summary>
        /// 冲刺按住（持续状态）
        /// </summary>
        public bool IsSprintHeld()
        {
            return UnityInput.GetKey(KeyCode.LeftShift);
        }

        /// <summary>
        /// 防御键按住（持续状态）— 鼠标右键
        /// </summary>
        public bool IsDefendHeld()
        {
            return UnityInput.GetMouseButton(1);  // Right mouse button
        }

        /// <summary>
        /// 2技能按下（Q键）- 普通攻击升级版
        /// </summary>
        public bool IsSkill2Pressed()
        {
            bool pressed = UnityInput.GetKeyDown(KeyCode.Q);
            if (pressed && !_skill2Consumed)
            {
                _skill2Consumed = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 3技能按下（R键）- 大招
        /// </summary>
        public bool IsSkill3Pressed()
        {
            bool pressed = UnityInput.GetKeyDown(KeyCode.R);
            if (pressed && !_skill3Consumed)
            {
                _skill3Consumed = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 3技能释放（R键松开）- 用于持续技能取消
        /// </summary>
        public bool IsSkill3Released()
        {
            return UnityInput.GetKeyUp(KeyCode.R);
        }

        /// <summary>
        /// 是否正在冲刺
        /// </summary>
        public bool IsSprinting()
        {
            return IsSprintHeld() && IsMoving();
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

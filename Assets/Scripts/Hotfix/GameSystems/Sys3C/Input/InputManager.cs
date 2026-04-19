using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using UnityInput = UnityEngine.Input;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 输入管理器 — 统一所有输入适配器，输出标准化 MoveCommand
    /// 同时提供跳跃/攻击/冲刺的即时输入检测
    /// </summary>
    public class InputManager
    {
        private readonly IInputAdapter _adapter;

        // 移动速度
        public float MoveSpeed { get; set; } = 5.0f;
        public float SprintSpeed { get; set; } = 9.0f;

        // 相机旋转灵敏度
        public float CameraSensitivityX { get; set; } = 2.0f;
        public float CameraSensitivityY { get; set; } = 2.0f;

        // 当前序列号（用于网络预测）
        private uint _sequence;

        // === 一次性事件（每帧只触发一次） ===
        private bool _jumpConsumed;
        private bool _attackConsumed;

        public InputManager()
        {
            _adapter = new KeyboardInputAdapter();
        }

        /// <summary>
        /// 每帧更新 — 重置即时事件状态
        /// </summary>
        public void Update()
        {
            // 每帧重置按下状态，供下一帧消费
            // 注意：Unity Input.GetButtonDown 在同一帧内多次调用返回相同值
        }

        /// <summary>
        /// 获取标准化移动命令
        /// </summary>
        public MoveCommand GetMoveCommand(Vector3 characterForward, Vector3 cameraForward)
        {
            Vector3 moveInput = _adapter.GetMoveInput();

            // 冲刺倍率
            float speed = IsSprintHeld() ? SprintSpeed : MoveSpeed;

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
                    Timestamp = DateTime.UtcNow.Ticks,
                    Sequence = ++_sequence
                };
            }

            return new MoveCommand
            {
                MoveDir = Vector3.zero,
                Speed = 0f,
                Rotation = Quaternion.identity,
                Timestamp = DateTime.UtcNow.Ticks,
                Sequence = ++_sequence
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
        /// 攻击按下（一次性事件）
        /// </summary>
        public bool IsAttackPressed()
        {
            // 鼠标左键
            bool pressed = UnityInput.GetMouseButtonDown(0);
            if (pressed && !_attackConsumed)
            {
                _attackConsumed = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 冲刺按住（持续状态）
        /// </summary>
        public bool IsSprintHeld()
        {
            return UnityInput.GetKey(KeyCode.LeftShift);
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

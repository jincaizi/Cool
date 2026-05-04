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
        private bool _attackConsumed;
        private bool _skill2Consumed;
        private bool _skill3Consumed;

        private void Awake()
        {
            _adapter = new KeyboardInputAdapter();
        }

        /// <summary>
        /// 每帧更新 — 重置即时事件状态
        /// </summary>
        public void Update()
        {
            // 每帧开始时重置消费标志，允许下一帧再次触发
            _jumpConsumed = false;
            _attackConsumed = false;
            _skill2Consumed = false;
            _skill3Consumed = false;
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

                if (Time.frameCount % 30 == 0)
                {
                    Debug.Log("[Input] MoveInput=" + moveInput + ", worldMoveDir=" + worldMoveDir
                        + ", sprint=" + sprintHeld);
                }

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

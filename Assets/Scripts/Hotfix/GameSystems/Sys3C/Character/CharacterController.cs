using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色控制器 — 移动/跳跃/物理驱动
    /// 只负责更新 CharacterData，状态变化通过事件通知
    /// </summary>
    public class CharacterController
    {
        private readonly UnityEngine.CharacterController _controller;
        private readonly Transform _transform;
        private readonly GroundDetector _groundDetector;

        // 移动参数
        public float MoveSpeed { get; set; } = 5.0f;
        public float SprintSpeed { get; set; } = 8.0f;
        public float RotationSpeed { get; set; } = 10.0f;
        public float Gravity { get; set; } = -30f;
        public float JumpForce { get; set; } = 12f;

        // 内部状态
        private CharacterData _data;
        private Vector3 _velocity;
        private bool _jumpRequested;

        // 事件
        public event Action OnJumpRequested;
        public event Action OnLanded;
        public event Action OnDeath;

        public CharacterData Data => _data;
        public bool IsGrounded => _groundDetector.IsGrounded();

        public CharacterController(
            Transform transform,
            UnityEngine.CharacterController controller,
            LayerMask groundLayer)
        {
            _transform = transform;
            _controller = controller;
            _groundDetector = new GroundDetector(controller);

            _data = new CharacterData
            {
                Position = transform.position,
                Rotation = transform.rotation,
                BaseState = BaseState.Idle,
                IsGrounded = true,
                IsDead = false
            };
        }

        /// <summary>
        /// 请求跳跃
        /// </summary>
        public void RequestJump()
        {
            if (_data.IsGrounded && !_data.IsDead && _data.BaseState != BaseState.JumpStart && _data.BaseState != BaseState.JumpAir)
            {
                _jumpRequested = true;
            }
        }

        /// <summary>
        /// 应用伤害（触发受击）
        /// </summary>
        public void ApplyHit()
        {
            // Hit 由 FSM 层处理，这里仅标记
        }

        /// <summary>
        /// 应用死亡
        /// </summary>
        public void ApplyDeath()
        {
            _data.IsDead = true;
            _data.BaseState = BaseState.Death;
            _velocity.y = 0f;
            OnDeath?.Invoke();
        }

        /// <summary>
        /// 每帧驱动
        /// </summary>
        public void Update(MoveCommand command)
        {
            if (_data.IsDead)
            {
                _data.Position = _transform.position;
                _data.Rotation = _transform.rotation;
                return;
            }

            bool wasGrounded = _data.IsGrounded;

            // 1. 应用水平移动
            float currentSpeed = command.IsSprint ? SprintSpeed : MoveSpeed;
            Vector3 moveVelocity = command.MoveDir * currentSpeed;
            moveVelocity.y = _velocity.y;
            _controller.Move(moveVelocity * Time.deltaTime);

            // 2. 检测地面
            _data.IsGrounded = _groundDetector.IsGrounded();

            // 3. 走下悬崖检测
            if (wasGrounded && !_data.IsGrounded && _data.BaseState != BaseState.JumpStart && _data.BaseState != BaseState.JumpAir)
            {
                _velocity.y = 0f;
            }

            // 4. 处理跳跃请求
            if (_jumpRequested && _data.IsGrounded)
            {
                _velocity.y = JumpForce;
                _jumpRequested = false;
                _data.BaseState = BaseState.JumpStart;
                OnJumpRequested?.Invoke();
            }

            // 5. 应用重力
            if (_data.BaseState == BaseState.JumpStart || _data.BaseState == BaseState.JumpAir || !_data.IsGrounded)
            {
                _velocity.y += Gravity * Time.deltaTime;
                _velocity.y = Mathf.Max(_velocity.y, -50f);

                // 额外Y轴移动
                Vector3 yMove = Vector3.up * _velocity.y * Time.deltaTime;
                _controller.Move(yMove);
            }
            else if (_data.IsGrounded)
            {
                _velocity.y = 0f;
            }

            // 6. 跳跃阶段转换
            UpdateJumpPhase();

            // 7. 基础移动状态（非跳跃时）
            UpdateBaseState(command, currentSpeed);

            // 8. 同步数据
            _data.Position = _transform.position;
            _data.Rotation = _transform.rotation;
            _data.Velocity = _controller.velocity;
            _data.VerticalVelocity = _velocity.y;
            _data.IsSprint = command.IsSprint;
        }

        private void UpdateJumpPhase()
        {
            if (_data.BaseState == BaseState.JumpStart)
            {
                // JumpStart 持续一帧后进入 JumpAir
                _data.BaseState = BaseState.JumpAir;
            }
            else if (_data.BaseState == BaseState.JumpAir)
            {
                // 着地检测
                if (_data.IsGrounded && _velocity.y <= 0)
                {
                    _data.BaseState = BaseState.JumpEnd;
                    _velocity.y = 0f;
                    OnLanded?.Invoke();
                }
            }
        }

        private void UpdateBaseState(MoveCommand command, float currentSpeed)
        {
            // 非跳跃期间管理基础状态
            if (_data.BaseState == BaseState.Idle ||
                _data.BaseState == BaseState.Move ||
                _data.BaseState == BaseState.Sprint)
            {
                if (command.MoveDir.sqrMagnitude > 0.01f)
                {
                    // 旋转
                    _transform.rotation = Quaternion.Slerp(
                        _transform.rotation,
                        command.Rotation,
                        RotationSpeed * Time.deltaTime
                    );

                    // 更新状态
                    if (command.IsSprint)
                        _data.BaseState = BaseState.Sprint;
                    else
                        _data.BaseState = BaseState.Move;
                }
                else
                {
                    _data.BaseState = BaseState.Idle;
                }
            }
        }

        /// <summary>
        /// 落地动画完成后调用
        /// </summary>
        public void FinishJump()
        {
            if (_data.BaseState == BaseState.JumpEnd)
            {
                _data.BaseState = BaseState.Idle;
            }
        }

        /// <summary>
        /// 应用服务端权威位置
        /// </summary>
        public void ApplyServerPosition(Vector3 position, Quaternion rotation)
        {
            _transform.position = position;
            _transform.rotation = rotation;
            _controller.enabled = false;
            _controller.transform.position = position;
            _controller.enabled = true;

            _data.Position = position;
            _data.Rotation = rotation;
        }
    }
}

using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Input;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色控制器 — 移动/转向/物理驱动
    /// </summary>
    public class CharacterController
    {
        private readonly UnityEngine.CharacterController _controller;
        private readonly Transform _transform;
        private readonly GroundDetector _groundDetector;

        // 移动参数
        public float MoveSpeed { get; set; } = 5.0f;
        public float RotationSpeed { get; set; } = 10.0f;
        public float Gravity { get; set; } = -30f;
        public float JumpForce { get; set; } = 12f;

        // 状态
        private CharacterData _data;
        private MoveCommand _currentCommand;
        private Vector3 _velocity;
        private bool _jumpRequested;

        /// <summary>
        /// 锁定状态不被 Update 覆盖（JumpEnd 播放期间）
        /// </summary>
        private bool _stateLocked;

        public CharacterData Data => _data;
        public bool IsGrounded => _groundDetector != null ? _groundDetector.IsGrounded() : _data.IsGrounded;

        /// <summary>
        /// 着地回调（由 CharacterAnimationDriver 注册）
        /// </summary>
        public event Action OnLanded;

        public CharacterController(
            Transform transform,
            UnityEngine.CharacterController controller,
            LayerMask groundLayer)
        {
            _transform = transform;
            _controller = controller;

            // 初始化地面检测器
            _groundDetector = new GroundDetector(transform, controller, groundLayer);

            _data = new CharacterData
            {
                Position = transform.position,
                Rotation = transform.rotation,
                State = CharacterState.Idle,
                IsGrounded = true
            };
        }

        /// <summary>
        /// 请求跳跃（由外部调用）
        /// </summary>
        public void RequestJump()
        {
            if (_data.IsGrounded)
            {
                _jumpRequested = true;
            }
        }

        /// <summary>
        /// 每帧驱动
        /// </summary>
        public void Update(MoveCommand command)
        {
            _currentCommand = command;
            _data.Position = _transform.position;
            _data.Rotation = _transform.rotation;
            _data.IsSprint = command.IsSprint;

            // 1. 先应用移动（让角色移动到新位置）
            bool wasGrounded = _data.IsGrounded;

            // 计算本帧移动向量
            Vector3 moveVelocity = command.MoveDir * command.Speed;
            moveVelocity.y = _velocity.y;
            _controller.Move(moveVelocity * Time.deltaTime);

            // 2. 移动后再检测地面（基于新位置）
            _data.IsGrounded = _groundDetector.IsGrounded();

            // 3. 检测走下悬崖
            if (wasGrounded && !_data.IsGrounded && !_jumpRequested && _data.JumpPhase == JumpPhase.None)
            {
                _velocity.y = 0f;
            }

            // 4. 处理跳跃请求
            bool isInJump = _data.JumpPhase == JumpPhase.Start || _data.JumpPhase == JumpPhase.Air;
            if (_jumpRequested && _data.IsGrounded)
            {
                _velocity.y = JumpForce;
                _jumpRequested = false;
                _data.JumpPhase = JumpPhase.Start;
                _data.State = CharacterState.JumpStart;
            }

            // 5. 应用重力
            if (isInJump || !_data.IsGrounded)
            {
                _velocity.y += Gravity * Time.deltaTime;
                _velocity.y = Mathf.Max(_velocity.y, -50f);
            }
            else if (_data.IsGrounded)
            {
                _velocity.y = 0f;
            }

            // 6. 地面/空中状态处理
            if (_data.IsGrounded && !isInJump)
            {
                // 地面移动
                if (command.MoveDir.sqrMagnitude > 0.01f)
                {
                    _stateLocked = false;
                    Quaternion targetRot = command.Rotation;
                    _transform.rotation = Quaternion.Slerp(
                        _transform.rotation,
                        targetRot,
                        RotationSpeed * Time.deltaTime
                    );
                    _data.State = command.IsSprint ? CharacterState.Run : CharacterState.Move;
                }
                else
                {
                    _data.State = CharacterState.Idle;
                }
            }
            else
            {
                // 空中状态
                _data.State = CharacterState.JumpAir;
                if (_data.JumpPhase == JumpPhase.Start)
                    _data.JumpPhase = JumpPhase.Air;
            }

            // 着地检测（在状态块外面，独立检查）
            if (_data.IsGrounded && _data.JumpPhase == JumpPhase.Air && _velocity.y <= 0)
            {
                UnityEngine.Debug.Log($"[Landing] detected! JumpPhase=Air->End, velocity.y={_velocity.y:F3}");
                _data.JumpPhase = JumpPhase.End;
                _data.State = CharacterState.JumpEnd;
                _stateLocked = true;
                OnLanded?.Invoke();
            }

            // 更新数据
            _data.Velocity = _controller.velocity;
            _data.VerticalVelocity = _velocity.y;
        }

        /// <summary>
        /// 应用服务端权威位置（网络校验后）
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

        /// <summary>
        /// 获取预测位置（用于网络同步）
        /// </summary>
        public Vector3 GetPredictedPosition()
        {
            return _transform.position;
        }

        public Quaternion GetPredictedRotation()
        {
            return _transform.rotation;
        }

        /// <summary>
        /// 跳跃落地动画完成时调用（由 CharacterAnimationDriver 在 JumpEnd 退出时调用）
        /// 注意：必须保持 JumpPhase > 0（设为 End=3），确保 JumpEnd→Idle 的 Animator 转换条件满足。
        /// JumpPhase > 0 且 IsJumping=false 时，转换才能正常触发。
        /// </summary>
        public void FinishJump()
        {
            UnityEngine.Debug.Log($"[FinishJump] stateLocked=false, jumpPhase={_data.JumpPhase}, state=Idle");
            _stateLocked = false;
            // 保持 JumpPhase = End (3)，确保 JumpEnd→Idle 转换条件 JumpPhase > 0 始终满足
            // IsJumping 在 JumpEnd_Enter 时已设为 false
            _data.JumpPhase = JumpPhase.End;
            _data.State = CharacterState.Idle;
        }
    }
}

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
                IsGrounded = true,
                JumpPhase = JumpPhase.None
            };
        }

        /// <summary>
        /// 请求跳跃（由外部调用）
        /// </summary>
        public void RequestJump()
        {
            if (_data.IsGrounded && _data.JumpPhase == JumpPhase.None)
            {
                _jumpRequested = true;
            }
        }

        /// <summary>
        /// 中断跳跃（被攻击或其他事件打断）
        /// </summary>
        public void AbortJump()
        {
            if (_data.JumpPhase == JumpPhase.Start || _data.JumpPhase == JumpPhase.Air)
            {
                _data.JumpPhase = JumpPhase.None;
                _velocity.y = 0f;
                _jumpRequested = false;
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

            // 记录上帧地面状态
            bool wasGrounded = _data.IsGrounded;
            JumpPhase prevJumpPhase = _data.JumpPhase;

            // ========== 1. 应用移动 ==========
            Vector3 moveVelocity = command.MoveDir * command.Speed;
            moveVelocity.y = _velocity.y;
            _controller.Move(moveVelocity * Time.deltaTime);

            // ========== 2. 检测地面（移动后） ==========
            _data.IsGrounded = _groundDetector.IsGrounded();

            // ========== 3. 走下悬崖检测 ==========
            // 从地面走到空中，且不是跳跃状态
            if (wasGrounded && !_data.IsGrounded && _data.JumpPhase == JumpPhase.None)
            {
                _velocity.y = 0f;  // 重置垂直速度
            }

            // ========== 4. 跳跃请求处理 ==========
            if (_jumpRequested && _data.IsGrounded && _data.JumpPhase == JumpPhase.None)
            {
                _velocity.y = JumpForce;
                _jumpRequested = false;
                _data.JumpPhase = JumpPhase.Start;
                _data.State = CharacterState.JumpStart;
            }

            // ========== 5. 应用重力 ==========
            bool isInJump = _data.JumpPhase == JumpPhase.Start || _data.JumpPhase == JumpPhase.Air;
            if (isInJump)
            {
                _velocity.y += Gravity * Time.deltaTime;
                _velocity.y = Mathf.Max(_velocity.y, -50f);  // 限制最大下落速度
            }
            else if (_data.IsGrounded)
            {
                _velocity.y = 0f;
            }
            else
            {
                // 非跳跃的空中状态（被击飞等），也应用重力
                _velocity.y += Gravity * Time.deltaTime * 0.5f;
                _velocity.y = Mathf.Max(_velocity.y, -50f);
            }

            // ========== 6. 跳跃阶段转换 ==========
            if (_data.JumpPhase == JumpPhase.Start && !_data.IsGrounded)
            {
                _data.JumpPhase = JumpPhase.Air;
            }

            // ========== 7. 着地检测 ==========
            if (_data.IsGrounded && prevJumpPhase == JumpPhase.Air && _velocity.y <= 0)
            {
                // 落地！
                _data.JumpPhase = JumpPhase.End;
                _data.State = CharacterState.JumpEnd;
                OnLanded?.Invoke();
            }

            // ========== 8. 状态处理 ==========
            // 如果状态被锁定（JumpEnd 播放期间），不覆盖状态
            if (!_stateLocked)
            {
                if (_data.IsGrounded && _data.JumpPhase == JumpPhase.None)
                {
                    // 地面状态
                    if (command.MoveDir.sqrMagnitude > 0.01f)
                    {
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
                else if (_data.JumpPhase == JumpPhase.Air)
                {
                    _data.State = CharacterState.JumpAir;
                }
                else if (_data.JumpPhase == JumpPhase.Start)
                {
                    _data.State = CharacterState.JumpStart;
                }
            }

            // ========== 9. 更新数据 ==========
            _data.Velocity = _controller.velocity;
            _data.VerticalVelocity = _velocity.y;
        }

        /// <summary>
        /// 完成跳跃（JumpEnd 动画退出时调用）
        /// </summary>
        public void FinishJump()
        {
            _stateLocked = false;
            _data.JumpPhase = JumpPhase.None;
            _data.State = CharacterState.Idle;
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
    }
}

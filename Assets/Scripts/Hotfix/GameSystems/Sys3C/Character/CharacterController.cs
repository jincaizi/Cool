using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Network;

namespace Hotfix.GameSystems.Sys3C.Character
{
    public class CharacterController
    {
        private readonly UnityEngine.CharacterController _controller;
        private readonly Transform _transform;
        private readonly GroundDetector _groundDetector;

        public float MoveSpeed { get; set; } = 5.0f;
        public float SprintSpeed { get; set; } = 8.0f;
        public float RotationSpeed { get; set; } = 10.0f;
        public float Gravity { get; set; } = -30f;
        public float JumpForce { get; set; } = 12f;

        private CharacterData _data;
        private Vector3 _velocity;
        private bool _jumpRequested;

        public event Action OnJumpRequested;
        public event Action OnLanded;
        public event Action OnDeath;

        public CharacterData Data => _data;
        public bool IsGrounded => _groundDetector.IsGrounded();

        // Network integration
        private NetworkPrediction _prediction;
        private NetworkBridge _bridge;
        private uint _currentSequence;

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
                IsDead = false,
                RequestJump = false
            };
        }

        public void InitializeNetwork(NetworkBridge bridge)
        {
            _bridge = bridge;
            _prediction = new NetworkPrediction();
            Debug.Log("[CharacterController] Network initialized");
        }

        public void RequestJump()
        {
            if (_data.IsGrounded && !_data.IsDead && _data.BaseState != BaseState.JumpStart && _data.BaseState != BaseState.JumpAir)
            {
                _jumpRequested = true;
            }
        }

        public void ApplyHit()
        {
            // Hit 由 FSM 层处理
        }

        public void ApplyDeath()
        {
            _data.IsDead = true;
            _data.BaseState = BaseState.Death;
            _velocity.y = 0f;
            OnDeath?.Invoke();
        }

        public void Update(MoveCommand command)
        {
            if (_data.IsDead)
            {
                _data.Position = _transform.position;
                _data.Rotation = _transform.rotation;
                return;
            }

            // 设置 RequestJump 标记
            _data.RequestJump = _jumpRequested;

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

                Vector3 yMove = Vector3.up * _velocity.y * Time.deltaTime;
                _controller.Move(yMove);
            }
            else if (_data.IsGrounded)
            {
                _velocity.y = 0f;
            }

            // 6. 跳跃阶段转换
            UpdateJumpPhase();

            // 7. 基础移动状态
            UpdateBaseState(command, currentSpeed);

            // 8. 同步数据
            _data.Position = _transform.position;
            _data.Rotation = _transform.rotation;
            _data.Velocity = _controller.velocity;
            _data.VerticalVelocity = _velocity.y;
            _data.IsSprint = command.IsSprint;

            // 9. 网络预测
            if (_prediction != null && _bridge != null)
            {
                _prediction.RecordPredictedFrame(_currentSequence, _data.Position, _data.Rotation);
                _bridge.SendInput(command, _currentSequence);

                if (_bridge.HasServerUpdate(out var seq, out var pos, out var rot))
                {
                    if (_prediction.ValidateAndCorrect(seq, pos, rot, out var corrected, out var correctedRot))
                    {
                        ApplyServerPosition(corrected.Position, correctedRot);
                    }
                }

                _currentSequence++;
            }
        }

        private void UpdateJumpPhase()
        {
            if (_data.BaseState == BaseState.JumpStart)
            {
                _data.BaseState = BaseState.JumpAir;
            }
            else if (_data.BaseState == BaseState.JumpAir)
            {
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
            if (_data.BaseState == BaseState.Idle ||
                _data.BaseState == BaseState.Move ||
                _data.BaseState == BaseState.Sprint)
            {
                if (command.MoveDir.sqrMagnitude > 0.01f)
                {
                    _transform.rotation = Quaternion.Slerp(
                        _transform.rotation,
                        command.Rotation,
                        RotationSpeed * Time.deltaTime
                    );

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

        public void FinishJump()
        {
            if (_data.BaseState == BaseState.JumpEnd)
            {
                _data.BaseState = BaseState.Idle;
            }
        }

        public void ApplyServerPosition(Vector3 position, Quaternion rotation)
        {
            // Rubber-band 平滑校正
            _transform.position = Vector3.Lerp(_transform.position, position, 0.5f);
            _transform.rotation = Quaternion.Slerp(_transform.rotation, rotation, 0.5f);

            _controller.enabled = false;
            _controller.transform.position = position;
            _controller.enabled = true;

            _data.Position = position;
            _data.Rotation = rotation;
        }
    }
}

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
        private readonly Rigidbody _rigidbody;
        private readonly GroundDetector _groundDetector;
        private readonly Transform _transform;

        // 移动参数
        public float MoveSpeed { get; set; } = 5.0f;
        public float RotationSpeed { get; set; } = 10.0f;
        public float Gravity { get; set; } = -20f;
        public float GroundCheckDistance { get; set; } = 0.1f;

        // 状态
        private CharacterData _data;
        private MoveCommand _currentCommand;
        private Vector3 _velocity;

        public CharacterData Data => _data;
        public bool IsGrounded => _data.IsGrounded;

        public CharacterController(
            Transform transform,
            UnityEngine.CharacterController controller,
            Rigidbody rigidbody,
            LayerMask groundLayer)
        {
            _transform = transform;
            _controller = controller;
            _rigidbody = rigidbody;
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
        /// 每帧驱动
        /// </summary>
        public void Update(MoveCommand command)
        {
            _currentCommand = command;

            // 更新地面状态
            _data.IsGrounded = _groundDetector.IsGrounded();
            _data.Position = _transform.position;
            _data.Rotation = _transform.rotation;

            // 移动逻辑
            if (_data.IsGrounded)
            {
                _velocity.y = Gravity * Time.deltaTime;

                if (command.MoveDir.sqrMagnitude > 0.01f)
                {
                    // 移动
                    Vector3 moveVelocity = command.MoveDir * command.Speed;
                    _rigidbody.velocity = new Vector3(moveVelocity.x, _velocity.y, moveVelocity.z);

                    // 转向
                    Quaternion targetRot = command.Rotation;
                    _transform.rotation = Quaternion.Slerp(
                        _transform.rotation,
                        targetRot,
                        RotationSpeed * Time.deltaTime
                    );

                    _data.State = CharacterState.Move;
                }
                else
                {
                    _rigidbody.velocity = new Vector3(0f, _velocity.y, 0f);
                    _data.State = CharacterState.Idle;
                }
            }
            else
            {
                // 空中
                _velocity.y += Gravity * Time.deltaTime;
                _rigidbody.velocity = new Vector3(_rigidbody.velocity.x, _velocity.y, _rigidbody.velocity.z);
                _data.State = CharacterState.JumpAir;
            }

            // 更新数据
            _data.Velocity = _rigidbody.velocity;
            _data.VerticalVelocity = _velocity.y;
        }

        /// <summary>
        /// 应用服务端权威位置（网络校验后）
        /// </summary>
        public void ApplyServerPosition(Vector3 position, Quaternion rotation)
        {
            _transform.position = position;
            _transform.rotation = rotation;
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;

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
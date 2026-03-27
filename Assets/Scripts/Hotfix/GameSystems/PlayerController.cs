using System;
using AOT.Core.GameEventDispatcher;
using AOT.Bridge;
using AOT.DataDefinition.Constants;
using AOT.DataDefinition.Enums;
using AOT.DataDefinition.Interfaces;
using UnityEngine;

namespace Hotfix.GameSystems
{
    /// <summary>
    /// 角色控制器，实现IPlayerController接口
    /// 支持移动、跳跃、攻击等行为，客户端预测
    /// </summary>
    public class PlayerController : IPlayerController
    {
        private GameObject? _characterGo;
        private CharacterController? _characterController;
        private Animator? _animator;
        private Transform? _transform;

        private Vector2 _moveInput;
        private Vector3 _velocity;
        private float _targetSpeed;
        private float _currentSpeed;
        private float _smoothSpeed = 0.1f;

        private float _walkSpeed = 3f;
        private float _runSpeed = 6f;
        private float _jumpForce = 5f;
        private float _gravity = -15f;
        private float _rotationSpeed = 10f;

        private bool _isAttacking;
        private float _attackDuration = 0.3f;
        private float _attackTimer;

        private Vector3 _lastServerPosition;
        private Quaternion _lastServerRotation;
        private bool _isPredicted;

        public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
        public bool IsMoving => _moveInput.magnitude > 0.01f;
        public bool IsGrounded => _characterController?.isGrounded ?? false;
        public float MoveSpeed
        {
            get => _runSpeed;
            set => _runSpeed = value;
        }
        public Transform? CharacterTransform => _transform;

        /// <summary>
        /// 初始化玩家控制器
        /// </summary>
        public void Initialize()
        {
            Bridge_Player.SetController(this);
            RegisterEventHandlers();
        }

        /// <summary>
        /// 注册事件监听
        /// </summary>
        private void RegisterEventHandlers()
        {
            GameEventDispatcher.Instance.Register(EventKeys.Move, OnMoveInput);
            GameEventDispatcher.Instance.Register(EventKeys.Jump, OnJump);
            GameEventDispatcher.Instance.Register(EventKeys.Attack, OnAttackInput);
            GameEventDispatcher.Instance.Register(EventKeys.AttackCancelled, OnAttackCancelled);
            GameEventDispatcher.Instance.Register(EventKeys.ServerPositionSync, OnServerPositionSync);
        }

        /// <summary>
        /// 设置角色对象
        /// </summary>
        public void SetCharacter(GameObject character)
        {
            _characterGo = character;
            _transform = character.transform;
            _characterController = character.GetComponent<CharacterController>();
            _animator = character.GetComponent<Animator>();

            if (_characterController == null)
            {
                _characterController = character.AddComponent<CharacterController>();
                _characterController.center = new Vector3(0, 1, 0);
                _characterController.radius = 0.3f;
                _characterController.height = 2f;
            }
        }

        /// <summary>
        /// 设置移动输入
        /// </summary>
        public void SetMoveInput(Vector2 input)
        {
            _moveInput = input;
        }

        /// <summary>
        /// 触发跳跃
        /// </summary>
        public void Jump()
        {
            if (!IsGrounded || CurrentState == PlayerState.Jump || CurrentState == PlayerState.Fall)
                return;

            _velocity.y = _jumpForce;
            SetState(PlayerState.Jump);
            Bridge_Player.DispatchJumped();
        }

        /// <summary>
        /// 触发攻击
        /// </summary>
        public void Attack()
        {
            if (_isAttacking) return;
            _isAttacking = true;
            _attackTimer = _attackDuration;
            SetState(PlayerState.Attack);
            Bridge_Player.DispatchAttacked();
        }

        /// <summary>
        /// 停止攻击
        /// </summary>
        public void StopAttack()
        {
            _isAttacking = false;
        }

        /// <summary>
        /// 服务器位置同步回调（客户端预测校正）
        /// </summary>
        public void OnServerPositionUpdate(Vector3 serverPosition, Quaternion serverRotation)
        {
            if (_transform == null) return;

            float distance = Vector3.Distance(_transform.position, serverPosition);
            if (distance > 0.1f)
            {
                _isPredicted = false;
                _lastServerPosition = serverPosition;
                _lastServerRotation = serverRotation;
            }
        }

        /// <summary>
        /// 更新控制器
        /// </summary>
        public void Update()
        {
            if (_characterController == null || _transform == null) return;

            UpdateMovement();
            UpdateGravity();
            UpdateAttack();
            UpdateAnimator();
            CorrectPrediction();

            if (IsMoving)
            {
                Bridge_Player.DispatchMoved(_transform.position, _currentSpeed);
            }
        }

        /// <summary>
        /// 更新移动
        /// </summary>
        private void UpdateMovement()
        {
            if (!IsGrounded && CurrentState != PlayerState.Jump)
                return;

            bool isSprinting = Bridge_Input.IsSprintPressed();
            _targetSpeed = isSprinting ? _runSpeed : (IsMoving ? _walkSpeed : 0f);
            _currentSpeed = Mathf.Lerp(_currentSpeed, _targetSpeed, _smoothSpeed);

            Vector3 moveDir = new Vector3(_moveInput.x, 0, _moveInput.y);

            if (Camera.main != null)
            {
                Vector3 camForward = Camera.main.transform.forward;
                Vector3 camRight = Camera.main.transform.right;
                camForward.y = 0;
                camRight.y = 0;
                camForward.Normalize();
                camRight.Normalize();
                moveDir = camForward * _moveInput.y + camRight * _moveInput.x;
            }

            Vector3 targetVelocity = moveDir * _currentSpeed;
            targetVelocity.y = _velocity.y;
            _velocity = Vector3.Lerp(_velocity, targetVelocity, _smoothSpeed);

            if (_characterController != null)
            {
                _characterController.Move(_velocity * Time.deltaTime);
            }

            if (IsMoving && _currentSpeed > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDir);
                _transform.rotation = Quaternion.Slerp(_transform.rotation, targetRotation, _rotationSpeed * Time.deltaTime);

                if (CurrentState != PlayerState.Attack && CurrentState != PlayerState.Jump)
                {
                    SetState(_currentSpeed >= _runSpeed * 0.9f ? PlayerState.Run : PlayerState.Walk);
                }
            }
            else if (CurrentState != PlayerState.Jump && CurrentState != PlayerState.Fall && CurrentState != PlayerState.Attack)
            {
                SetState(PlayerState.Idle);
            }
        }

        /// <summary>
        /// 更新重力
        /// </summary>
        private void UpdateGravity()
        {
            if (_characterController == null) return;

            if (_characterController.isGrounded && _velocity.y < 0)
            {
                _velocity.y = -2f;
                if (CurrentState == PlayerState.Jump)
                {
                    SetState(IsMoving ? (_currentSpeed >= _runSpeed * 0.9f ? PlayerState.Run : PlayerState.Walk) : PlayerState.Idle);
                }
            }
            else
            {
                _velocity.y += _gravity * Time.deltaTime;
                if (_velocity.y < 0 && CurrentState == PlayerState.Jump)
                {
                    SetState(PlayerState.Fall);
                }
            }
        }

        /// <summary>
        /// 更新攻击
        /// </summary>
        private void UpdateAttack()
        {
            if (!_isAttacking) return;

            _attackTimer -= Time.deltaTime;
            if (_attackTimer <= 0)
            {
                _isAttacking = false;
                if (IsGrounded && CurrentState == PlayerState.Attack)
                {
                    SetState(IsMoving ? (_currentSpeed >= _runSpeed * 0.9f ? PlayerState.Run : PlayerState.Walk) : PlayerState.Idle);
                }
            }
        }

        /// <summary>
        /// 更新动画状态机
        /// </summary>
        private void UpdateAnimator()
        {
            if (_animator == null) return;

            _animator.SetFloat("Speed", _currentSpeed);
            _animator.SetFloat("VerticalVelocity", _velocity.y);
            _animator.SetBool("IsGrounded", IsGrounded);
            _animator.SetBool("IsAttacking", _isAttacking);
        }

        /// <summary>
        /// 校正客户端预测误差
        /// </summary>
        private void CorrectPrediction()
        {
            if (_isPredicted) return;
            if (_transform == null) return;

            _transform.position = Vector3.Lerp(_transform.position, _lastServerPosition, Time.deltaTime * 10f);
            _transform.rotation = Quaternion.Slerp(_transform.rotation, _lastServerRotation, Time.deltaTime * 10f);

            if (Vector3.Distance(_transform.position, _lastServerPosition) < 0.05f)
            {
                _isPredicted = true;
            }
        }

        /// <summary>
        /// 设置状态
        /// </summary>
        private void SetState(PlayerState newState)
        {
            if (CurrentState == newState) return;

            PlayerState oldState = CurrentState;
            CurrentState = newState;
            Bridge_Player.DispatchStateChanged(oldState, newState);
        }

        private void OnMoveInput(object data)
        {
            if (data is Vector2 input)
            {
                SetMoveInput(input);
            }
        }

        private void OnJump(object _)
        {
            Jump();
        }

        private void OnAttackInput(object _)
        {
            Attack();
        }

        private void OnAttackCancelled(object _)
        {
            StopAttack();
        }

        private void OnServerPositionSync(object data)
        {
            if (data is ServerPositionSyncData syncData)
            {
                OnServerPositionUpdate(syncData.Position, syncData.Rotation);
            }
        }

        /// <summary>
        /// 销毁
        /// </summary>
        public void Destroy()
        {
            GameEventDispatcher.Instance.Unregister(EventKeys.Move, OnMoveInput);
            GameEventDispatcher.Instance.Unregister(EventKeys.Jump, OnJump);
            GameEventDispatcher.Instance.Unregister(EventKeys.Attack, OnAttackInput);
            GameEventDispatcher.Instance.Unregister(EventKeys.AttackCancelled, OnAttackCancelled);
            GameEventDispatcher.Instance.Unregister(EventKeys.ServerPositionSync, OnServerPositionSync);

            Bridge_Player.SetController(null!);
        }

        /// <summary>
        /// 服务器位置同步数据
        /// </summary>
        public struct ServerPositionSyncData
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }
    }
}

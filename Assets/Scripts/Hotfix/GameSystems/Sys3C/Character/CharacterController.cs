using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Network;
using Hotfix.GameSystems.Skills.Effect;

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

        /// <summary>
        /// 锁定旋转（技能播放时使用）
        /// </summary>
        public bool LockRotation { get; set; }

        public CharacterData Data => _data;
        public bool IsGrounded => _groundDetector.IsGrounded();
        public Transform Transform => _transform;

        // 适配器引用
        private CharacterStatsAdapter _statsAdapter;
        private ShieldSystemAdapter _shieldSystemAdapter;
        private PhysicsSystemAdapter _physicsSystemAdapter;
        private StatusControllerAdapter _statusControllerAdapter;

        /// <summary>
        /// 角色属性适配器
        /// </summary>
        public CharacterStatsAdapter StatsAdapter
        {
            get
            {
                if (_statsAdapter == null)
                    _statsAdapter = new CharacterStatsAdapter();
                return _statsAdapter;
            }
        }

        /// <summary>
        /// 护盾系统适配器
        /// </summary>
        public ShieldSystemAdapter ShieldSystemAdapter
        {
            get
            {
                if (_shieldSystemAdapter == null)
                    _shieldSystemAdapter = new ShieldSystemAdapter();
                return _shieldSystemAdapter;
            }
        }

        /// <summary>
        /// 物理系统适配器
        /// </summary>
        public PhysicsSystemAdapter PhysicsSystemAdapter
        {
            get
            {
                if (_physicsSystemAdapter == null)
                    _physicsSystemAdapter = new PhysicsSystemAdapter(this);
                return _physicsSystemAdapter;
            }
        }

        /// <summary>
        /// 状态控制器适配器
        /// </summary>
        public StatusControllerAdapter StatusControllerAdapter
        {
            get
            {
                if (_statusControllerAdapter == null)
                    _statusControllerAdapter = new StatusControllerAdapter();
                return _statusControllerAdapter;
            }
        }

        /// <summary>
        /// 设置基础状态
        /// </summary>
        public void SetBaseState(BaseState state)
        {
            _data.BaseState = state;
        }

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

            // 检查眩晕状态
            if (StatusControllerAdapter.IsStunned)
            {
                // 眩晕中只处理重力
                _velocity.y += Gravity * Time.deltaTime;
                _velocity.y = Mathf.Max(_velocity.y, -50f);
                Vector3 yMove = Vector3.up * _velocity.y * Time.deltaTime;
                _controller.Move(yMove);

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
                SetBaseState(BaseState.JumpStart);
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
            _data.MoveDir = command.MoveDir;

            // 9. 更新护盾和状态
            _shieldSystemAdapter?.Update(Time.deltaTime);
            _statusControllerAdapter?.Update(Time.deltaTime);

            // 10. 网络预测（暂时禁用，等AOT层实现）
            // if (_prediction != null && _bridge != null)
            // {
            //     _prediction.RecordPredictedFrame(_currentSequence, _data.Position, _data.Rotation);
            //     _bridge.SendInput(command, _currentSequence);
            //
            //     if (_bridge.HasServerUpdate(out var seq, out var pos, out var rot))
            //     {
            //         if (_prediction.ValidateAndCorrect(seq, pos, rot, out var corrected, out var correctedRot))
            //         {
            //             ApplyServerPosition(corrected.Position, correctedRot);
            //         }
            //     }
            //
            //     _currentSequence++;
            // }
        }

        private void UpdateJumpPhase()
        {
            if (_data.BaseState == BaseState.JumpStart)
            {
                SetBaseState(BaseState.JumpAir);
            }
            else if (_data.BaseState == BaseState.JumpAir)
            {
                if (_data.IsGrounded && _velocity.y <= 0)
                {
                    SetBaseState(BaseState.JumpEnd);
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
                    // 技能播放期间不更新朝向，让角色保持动画朝向
                    if (!LockRotation)
                    {
                        _transform.rotation = Quaternion.Slerp(
                            _transform.rotation,
                            command.Rotation,
                            RotationSpeed * Time.deltaTime
                        );
                    }

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

        /// <summary>
        /// 应用伤害
        /// </summary>
        public void TakeDamage(float damage, DamageType damageType)
        {
            // 先检查护盾
            float actualDamage = damage;
            if (_shieldSystemAdapter != null)
            {
                float absorbed = _shieldSystemAdapter.AbsorbDamage(damage);
                actualDamage = damage - absorbed;
            }

            // 应用伤害到属性
            if (actualDamage > 0)
            {
                float currentHealth = StatsAdapter.GetAttribute(AttributeType.Health);
                StatsAdapter.SetBaseAttribute(AttributeType.Health, Mathf.Max(0, currentHealth - actualDamage));

                Debug.Log($"[CharacterController] TakeDamage: {actualDamage} (absorbed: {damage - actualDamage})");

                // 检查死亡
                if (StatsAdapter.GetAttribute(AttributeType.Health) <= 0)
                {
                    ApplyDeath();
                }
            }
        }

        /// <summary>
        /// 治疗
        /// </summary>
        public void Heal(float amount)
        {
            float currentHealth = StatsAdapter.GetAttribute(AttributeType.Health);
            float maxHealth = StatsAdapter.GetMaxHealth();
            StatsAdapter.SetBaseAttribute(AttributeType.Health, Mathf.Min(maxHealth, currentHealth + amount));
        }
    }
}
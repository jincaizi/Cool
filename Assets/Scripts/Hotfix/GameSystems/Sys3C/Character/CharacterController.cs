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
        private float _leftGroundTriggerTime = -1f; // LeftGround 事件触发时间

        public event Action OnJumpRequested;
        public event Action OnLanded;
        public event Action OnLeftGround;  // 离开地面事件（用于受击判定）
        public event Action OnDeath;

        /// <summary>
        /// 锁定旋转（技能播放时使用）
        /// </summary>
        public bool LockRotation { get; set; }

        /// <summary>
        /// 锁定移动（突进时使用）
        /// </summary>
        public bool LockMovement { get; set; }

        private bool _isDefending;
        public bool IsDefending => _isDefending;

        // 盾耐久
        private float _shieldDurability;
        private const float MaxShieldDurability = 50f;
        private const float DefendSpeedMultiplier = 0.4f;

        public float ShieldDurabilityPercent =>
            _shieldDurability / MaxShieldDurability;

        public bool IsShieldBroken => _shieldDurability <= 0f;

        public CharacterData Data => _data;
        public bool IsGrounded => _groundDetector.IsGrounded();
        public Transform Transform => _transform;

        // 适配器引用
        private CharacterStatsAdapter _statsAdapter;
        private ShieldSystemAdapter _shieldSystemAdapter;
        private PhysicsSystemAdapter _physicsSystemAdapter;
        private StatusControllerAdapter _statusControllerAdapter;

        // StateCoordinator 引用（用于获取击退位移）
        private Core.StateCoordinator _stateCoordinator;

        /// <summary>
        /// 设置状态协调器引用
        /// </summary>
        public void SetStateCoordinator(Core.StateCoordinator coordinator)
        {
            _stateCoordinator = coordinator;
        }

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
            // 死亡状态：只同步位置
            if (_data.IsDead)
            {
                SyncPositionAndRotation();
                return;
            }

            // 防御姿态：限制移动速度 + 禁用跳跃
            if (_isDefending)
            {
                _data.RequestJump = false;
                _jumpRequested = false;
            }

            // 眩晕状态：只处理重力
            if (StatusControllerAdapter.IsStunned)
            {
                UpdateStunnedGravity();
                SyncPositionAndRotation();
                return;
            }

            // 设置跳跃请求标记
            _data.RequestJump = _jumpRequested;

            bool wasGrounded = _data.IsGrounded;

            // 应用水平移动
            ApplyHorizontalMovement(command);

            // 检测地面状态
            UpdateGroundDetection(wasGrounded);

            // 处理跳跃请求
            ProcessJumpRequest();

            // 处理 LeftGround 触发
            ProcessLeftGroundTrigger();

            // 应用重力
            ApplyGravity();

            // 更新跳跃阶段
            UpdateJumpPhase();

            // 更新基础移动状态
            UpdateBaseState(command);

            // 同步数据
            SyncDataAfterUpdate(command);

            // 应用受击位移（击退/浮空）
            ApplyHitDisplacement();
        }

        /// <summary>
        /// 死亡状态处理：只同步位置
        /// </summary>
        private void SyncPositionAndRotation()
        {
            _data.Position = _transform.position;
            _data.Rotation = _transform.rotation;
        }

        /// <summary>
        /// 眩晕状态：只处理重力
        /// </summary>
        private void UpdateStunnedGravity()
        {
            _velocity.y += Gravity * Time.deltaTime;
            _velocity.y = Mathf.Max(_velocity.y, -50f);
            Vector3 yMove = Vector3.up * _velocity.y * Time.deltaTime;
            _controller.Move(yMove);
        }

        /// <summary>
        /// 应用水平移动
        /// </summary>
        private void ApplyHorizontalMovement(MoveCommand command)
        {
            // 突进时不允许普通移动
            if (LockMovement)
            {
                return;
            }

            float currentSpeed = command.IsSprint ? SprintSpeed : MoveSpeed;

            // 防御中减速
            if (_isDefending)
                currentSpeed *= DefendSpeedMultiplier;

            Vector3 moveVelocity = command.MoveDir * currentSpeed;
            moveVelocity.y = _velocity.y;
            _controller.Move(moveVelocity * Time.deltaTime);
        }

        /// <summary>
        /// 更新地面检测
        /// </summary>
        private void UpdateGroundDetection(bool wasGrounded)
        {
            _data.IsGrounded = _groundDetector.IsGrounded();

            // 走下悬崖检测
            if (wasGrounded && !_data.IsGrounded &&
                _data.BaseState != BaseState.JumpStart &&
                _data.BaseState != BaseState.JumpAir)
            {
                _velocity.y = 0f;
            }
        }

        /// <summary>
        /// 处理跳跃请求
        /// </summary>
        private void ProcessJumpRequest()
        {
            if (_jumpRequested && _data.IsGrounded)
            {
                _velocity.y = JumpForce;
                _jumpRequested = false;
                SetBaseState(BaseState.JumpStart);
                OnJumpRequested?.Invoke();

                // 延迟触发 LeftGround 事件（用于受击判定）
                _leftGroundTriggerTime = Time.time + 0.1f;
            }
        }

        /// <summary>
        /// 处理 LeftGround 触发检测
        /// </summary>
        private void ProcessLeftGroundTrigger()
        {
            if (_leftGroundTriggerTime > 0 && Time.time >= _leftGroundTriggerTime)
            {
                if (_data.BaseState == BaseState.JumpStart || _data.BaseState == BaseState.JumpAir)
                {
                    _data.IsGrounded = false;
                    _data.HasLeftGround = true;
                    OnLeftGround?.Invoke();
                }
                _leftGroundTriggerTime = -1f;
            }

            // 落地时重置 HasLeftGround
            if (_data.IsGrounded && _data.HasLeftGround)
            {
                _data.HasLeftGround = false;
            }
        }

        /// <summary>
        /// 应用重力
        /// </summary>
        private void ApplyGravity()
        {
            bool isInAir = _data.BaseState == BaseState.JumpStart ||
                          _data.BaseState == BaseState.JumpAir ||
                          !_data.IsGrounded;

            if (isInAir)
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
        }

        /// <summary>
        /// 同步数据到 _data
        /// </summary>
        private void SyncDataAfterUpdate(MoveCommand command)
        {
            _data.Position = _transform.position;
            _data.Rotation = _transform.rotation;
            _data.Velocity = _controller.velocity;
            _data.VerticalVelocity = _velocity.y;
            _data.IsSprint = command.IsSprint;
            _data.MoveDir = command.MoveDir;

            // 计算移动幅度（用于 Blend Tree）
            _data.MoveMagnitude = command.MoveDir.magnitude;

            // 计算移动速度（0-1，用于 Blend Tree 混合）
            // Blend Tree 阈值：0=Idle, 0.5=Walk/Move, 1=Sprint
            if (command.MoveDir.sqrMagnitude < 0.01f)
            {
                // 不移动
                _data.MovementSpeed = 0f;
            }
            else if (command.IsSprint)
            {
                // 冲刺：Blend = 1.0
                _data.MovementSpeed = 1f;
            }
            else
            {
                // 普通移动/Walk：Blend = 0.5
                _data.MovementSpeed = 0.5f;
            }

            // 更新护盾和状态
            _shieldSystemAdapter?.Update(Time.deltaTime);
            _statusControllerAdapter?.Update(Time.deltaTime);
        }

        /// <summary>
        /// 应用受击位移
        /// </summary>
        private void ApplyHitDisplacement()
        {
            if (_stateCoordinator == null) return;

            var displacement = _stateCoordinator.GetKnockbackDisplacement();
            if (displacement.sqrMagnitude > 0.001f)
            {
                _controller.Move(displacement);
                _data.Position = _transform.position;
            }
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

        /// <summary>
        /// 更新基础移动状态
        /// </summary>
        private void UpdateBaseState(MoveCommand command)
        {
            if (_data.BaseState == BaseState.Idle ||
                _data.BaseState == BaseState.Move ||
                _data.BaseState == BaseState.Sprint)
            {
                // 技能/移动锁定时不更新基础状态，防止动画混合树显示行走动画
                if (LockMovement)
                    return;

                if (command.MoveDir.sqrMagnitude > 0.01f)
                {
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

        /// <summary>
        /// 尝试进入防御姿态。条件：着地、未死亡、未受击、未已在防御。
        /// </summary>
        public bool TryEnterDefend()
        {
            if (!_data.IsGrounded || _data.IsDead || _isDefending)
                return false;

            _isDefending = true;
            _data.IsDefending = true;
            _shieldDurability = MaxShieldDurability;
            return true;
        }

        /// <summary>
        /// 退出防御姿态。
        /// </summary>
        public void TryExitDefend()
        {
            if (!_isDefending) return;

            _isDefending = false;
            _data.IsDefending = false;
        }

        /// <summary>
        /// 盾吸收伤害，返回是否盾已破。
        /// </summary>
        public bool AbsorbDamage(float absorbedAmount)
        {
            _shieldDurability -= absorbedAmount;
            return _shieldDurability <= 0f;
        }

        /// <summary>
        /// 盾被打破，强制退出防御。
        /// </summary>
        public void OnShieldBreak()
        {
            _isDefending = false;
            _data.IsDefending = false;
            _shieldDurability = 0f;
        }
    }
}
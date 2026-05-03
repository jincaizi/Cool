using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Character;

namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 状态协调器 - 管理三层 FSM 的协调
    /// </summary>
    public class StateCoordinator : IStateCoordinator
    {
        private readonly BaseFSM _baseFSM;
        private readonly AttackFSM _attackFSM;
        private readonly HitFSM _hitFSM;
        private readonly FSMConfig _config;

        private LayerType _activeLayer = LayerType.Base;
        private LayerType _lockedLayer = LayerType.Base;
        private float _resistance;

        public LayerType ActiveLayer => _activeLayer;
        public HitFSM HitFSM => _hitFSM;

        /// <summary>
        /// 是否可以移动：非Hit层时可以移动
        /// </summary>
        public bool CanMove => _activeLayer != LayerType.Hit;

        /// <summary>
        /// 是否可以攻击：非Hit层时可以攻击
        /// </summary>
        public bool CanAttack => _activeLayer != LayerType.Hit;

        /// <summary>
        /// 是否有霸体（受击时不处理伤害）
        /// </summary>
        public bool HasSuperArmor => _attackFSM.HasSuperArmor || _hitFSM.HasSuperArmor;

        /// <summary>
        /// 是否处于免疫状态（死亡）
        /// </summary>
        public bool IsImmune => _hitFSM.HasSuperArmor;

        public StateCoordinator(BaseFSM baseFSM, AttackFSM attackFSM, HitFSM hitFSM)
            : this(baseFSM, attackFSM, hitFSM, FSMConfig.Default)
        {
        }

        public StateCoordinator(BaseFSM baseFSM, AttackFSM attackFSM, HitFSM hitFSM, FSMConfig config)
        {
            _baseFSM = baseFSM;
            _attackFSM = attackFSM;
            _hitFSM = hitFSM;
            _config = config;
            _resistance = config.MaxResistance;
        }

        /// <summary>
        /// 初始化协调器
        /// </summary>
        public void Initialize()
        {
            // 订阅各层事件
            _baseFSM.OnStateChanged += OnBaseStateChanged;
            _attackFSM.OnAttackCompleted += OnAttackCompleted;
            _attackFSM.OnSkillCompleted += OnSkillCompleted;
            _hitFSM.OnStateChanged += OnHitStateChanged;
            _hitFSM.OnHitComplete += OnHitComplete;
            _hitFSM.OnDeathComplete += OnDeathComplete;
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            _hitFSM.Update(deltaTime);
        }

        /// <summary>
        /// 获取 HitFSM（用于外部访问）
        /// </summary>
        public HitFSM HitFSM => _hitFSM;

        /// <summary>
        /// 设置活跃层（供 FSMManager 调用）
        /// </summary>
        public void SetActiveLayer(LayerType layer)
        {
            SetActiveLayerInternal(layer);
        }

        /// <summary>
        /// 请求普通攻击
        /// </summary>
        public bool TryRequestAttack()
        {
            if (_activeLayer == LayerType.Hit) return false;
            if (_activeLayer == LayerType.Attack) return true; // 已经在攻击

            // 锁定 Base 层
            LockLayer(LayerType.Base);
            SetActiveLayer(LayerType.Attack);

            _attackFSM.RequestNormalAttack();
            EventBus.Emit(new SkillActivatedEvent(0, "NormalAttack"));
            return true;
        }

        /// <summary>
        /// 请求技能
        /// </summary>
        public bool TryRequestSkill(int skillId, string skillName)
        {
            if (_activeLayer == LayerType.Hit) return false;
            if (!_attackFSM.CanPlaySkill) return false;

            LockLayer(LayerType.Base);
            SetActiveLayer(LayerType.Attack);

            // 根据 skillId 触发对应技能
            if (skillName == "SkillQ")
            {
                _attackFSM.RequestSkillQ();
            }
            else if (skillName == "SkillR")
            {
                _attackFSM.RequestSkillR(_baseFSM.CurrentState == BaseState.JumpAir ||
                                         _baseFSM.CurrentState == BaseState.JumpEnd);
            }

            EventBus.Emit(new SkillActivatedEvent(skillId, skillName));
            return true;
        }

        /// <summary>
        /// 请求跳跃
        /// </summary>
        public bool TryRequestJump()
        {
            if (_activeLayer == LayerType.Hit) return false;
            if (_activeLayer == LayerType.Attack) return false; // 攻击中不能跳跃

            EventBus.Emit(JumpEvent.Start);
            return true;
        }

        /// <summary>
        /// 处理伤害
        /// </summary>
        public void HandleDamage(DamageEvent damage)
        {
            // 死亡状态不处理伤害
            if (_hitFSM.HasSuperArmor) return;

            // 检查霸体
            if (_attackFSM.HasSuperArmor || _attackFSM.SuperArmorRemaining > 0)
            {
                Debug.Log("[StateCoordinator] Damage blocked by super armor");
                return;
            }

            // 消耗韧性
            _resistance -= damage.Damage * 0.5f;
            if (_resistance < 0) _resistance = 0;

            // 构建受击数据
            var hitData = new HitData
            {
                Damage = damage.Damage,
                HitDirection = damage.HitDirection,
                KnockbackForce = damage.KnockbackForce,
                LaunchForce = damage.LaunchForce,
                StunDuration = damage.StunDuration,
                IsCritical = damage.IsCritical
            };

            // 进入受击状态
            if (_hitFSM.EnterHit(hitData))
            {
                // Hit 层打断一切
                SetActiveLayer(LayerType.Hit);
                _attackFSM.ForceIdle();

                // 锁定 Base 层
                LockLayer(LayerType.Base);
                _baseFSM.LockState(BaseState.Idle);

                EventBus.Emit(damage);
                EventBus.Emit(new HitReceivedEvent());
            }

            Debug.Log($"[StateCoordinator] Damage handled: {damage.Damage}, Resistance: {_resistance}");
        }

        /// <summary>
        /// 进入死亡状态
        /// </summary>
        public void HandleDeath()
        {
            _hitFSM.EnterDeath();
            SetActiveLayer(LayerType.Hit);
            _baseFSM.LockState(BaseState.Death);
            _attackFSM.ForceIdle();
            LockLayer(LayerType.Base);
        }

        /// <summary>
        /// 复活
        /// </summary>
        public void HandleResurrect()
        {
            _hitFSM.Resurrect();
            _resistance = _config.MaxResistance;
            UnlockAndReturnToBase();
        }

        /// <summary>
        /// 霸体检查
        /// </summary>
        public bool HasSuperArmorAgainst(InterruptionSource source)
        {
            return HasSuperArmor;
        }

        /// <summary>
        /// 检查层是否被锁定
        /// </summary>
        public bool IsLayerLocked(LayerType layer)
        {
            return _lockedLayer == layer;
        }

        /// <summary>
        /// 获取韧性值
        /// </summary>
        public float GetResistance() => _resistance;

        /// <summary>
        /// 恢复韧性
        /// </summary>
        public void RestoreResistance(float amount)
        {
            _resistance = Mathf.Min(_resistance + amount, _config.MaxResistance);
        }

        /// <summary>
        /// 获取受击位移（用于应用击退效果）
        /// </summary>
        public Vector3 GetKnockbackDisplacement()
        {
            return _hitFSM.GetKnockbackDisplacement();
        }

        /// <summary>
        /// 检查角色是否处于空中受击状态
        /// </summary>
        public bool IsInAirHit()
        {
            return _hitFSM.CurrentState == HitState.Launched;
        }

        /// <summary>
        /// 获取当前活跃层的状态描述
        /// </summary>
        public string GetActiveStateDescription()
        {
            return $"[Layer: {_activeLayer}] Base={_baseFSM.CurrentState}, Attack={_attackFSM.CurrentState}, Hit={_hitFSM.CurrentState}";
        }

        /// <summary>
        /// 获取当前状态
        /// </summary>
        public string GetCurrentState(LayerType layer)
        {
            return layer switch
            {
                LayerType.Base => _baseFSM.CurrentState.ToString(),
                LayerType.Attack => _attackFSM.CurrentState.ToString(),
                LayerType.Hit => _hitFSM.CurrentState.ToString(),
                _ => "Unknown"
            };
        }

        /// <summary>
        /// 获取当前状态枚举值
        /// </summary>
        public T GetCurrentState<T>(LayerType layer) where T : Enum
        {
            if (layer == LayerType.Base)
                return (T)(object)_baseFSM.CurrentState;
            if (layer == LayerType.Attack)
                return (T)(object)_attackFSM.CurrentState;
            if (layer == LayerType.Hit)
                return (T)(object)_hitFSM.CurrentState;

            return default;
        }

        /// <summary>
        /// 解锁层并返回 Base
        /// </summary>
        public void UnlockAndReturnToBase()
        {
            _lockedLayer = LayerType.Base;
            SetActiveLayer(LayerType.Base);
            _baseFSM.Unlock(BaseState.Idle);
            EventBus.Emit(new LayerUnlockedEvent(LayerType.Base));
        }

        private void SetActiveLayerInternal(LayerType layer)
        {
            if (_activeLayer != layer)
            {
                var previous = _activeLayer;
                _activeLayer = layer;
                EventBus.Emit(new StateChangedEvent(layer, previous.ToString(), layer.ToString()));
            }
        }

        private void LockLayer(LayerType layer)
        {
            if (_lockedLayer != layer)
            {
                _lockedLayer = layer;
                EventBus.Emit(new LayerLockedEvent(layer, true));
            }
        }

        private void OnBaseStateChanged(BaseState state)
        {
            EventBus.Emit(new StateChangedEvent(LayerType.Base, _baseFSM.CurrentState.ToString(), state.ToString()));

            // 跳跃结束后解锁
            if (state == BaseState.Idle && _activeLayer != LayerType.Attack)
            {
                UnlockAndReturnToBase();
            }
        }

        private void OnAttackCompleted()
        {
            if (_attackFSM.CurrentState == AttackState.Idle)
            {
                UnlockAndReturnToBase();
            }
            EventBus.Emit(new SkillCompletedEvent(0, false));
        }

        private void OnSkillCompleted()
        {
            UnlockAndReturnToBase();
            EventBus.Emit(new SkillCompletedEvent(0, false));
        }

        private void OnHitStateChanged(HitFSM.HitState state)
        {
            Debug.Log($"[StateCoordinator] HitState changed to: {state}");
            EventBus.Emit(new StateChangedEvent(LayerType.Hit, "", state.ToString()));
        }

        private void OnHitComplete()
        {
            Debug.Log("[StateCoordinator] Hit complete, returning to base");
            UnlockAndReturnToBase();
        }

        private void OnDeathComplete()
        {
            Debug.Log("[StateCoordinator] Death complete, ready for resurrect");
            EventBus.Emit(new ResurrectEvent(0));
        }
    }
}
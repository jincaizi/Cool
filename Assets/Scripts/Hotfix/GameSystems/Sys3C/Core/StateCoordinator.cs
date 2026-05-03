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
    public class StateCoordinator
    {
        private readonly BaseFSM _baseFSM;
        private readonly AttackFSM _attackFSM;
        private object _hitFSM; // 暂时为 null，等 HitFSM 实现后完善

        private LayerType _activeLayer = LayerType.Base;
        private LayerType _lockedLayer = LayerType.Base;

        public LayerType ActiveLayer => _activeLayer;
        public bool CanMove => _activeLayer == LayerType.Base || _activeLayer == LayerType.Attack;
        public bool CanAttack => _activeLayer != LayerType.Hit && _activeLayer != LayerType.Base;
        public bool HasSuperArmor => false; // AttackFSM 暂时没有霸体属性

        public StateCoordinator(BaseFSM baseFSM, AttackFSM attackFSM)
        {
            _baseFSM = baseFSM;
            _attackFSM = attackFSM;
        }

        /// <summary>
        /// 初始化协调器
        /// </summary>
        public void Initialize(object hitFSM)
        {
            _hitFSM = hitFSM;

            // 订阅各层事件
            _baseFSM.OnStateChanged += OnBaseStateChanged;
            _attackFSM.OnAttackCompleted += OnAttackCompleted;
            _attackFSM.OnSkillCompleted += OnSkillCompleted;
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
            if (HasSuperArmor) return; // 有霸体不处理

            // Hit 层打断一切
            SetActiveLayer(LayerType.Hit);
            _attackFSM.ForceIdle();

            EventBus.Emit(damage);
            EventBus.Emit(new HitReceivedEvent());

            Debug.Log($"[StateCoordinator] Damage handled: {damage.Damage}");
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
        /// 获取当前状态
        /// </summary>
        public string GetCurrentState(LayerType layer)
        {
            return layer switch
            {
                LayerType.Base => _baseFSM.CurrentState.ToString(),
                LayerType.Attack => _attackFSM.CurrentState.ToString(),
                LayerType.Hit => _hitFSM?.ToString() ?? "None",
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
            if (layer == LayerType.Hit && _hitFSM != null)
                return default; // HitFSM 还未实现

            return default;
        }

        /// <summary>
        /// 解锁层并返回 Base
        /// </summary>
        public void UnlockAndReturnToBase()
        {
            _lockedLayer = LayerType.Base;
            SetActiveLayer(LayerType.Base);
            EventBus.Emit(new LayerUnlockedEvent(LayerType.Base));
        }

        private void SetActiveLayer(LayerType layer)
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
    }
}
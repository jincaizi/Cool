using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 状态协调器 - 管理三层 FSM 的协调
    /// 注意：此类需要 FSM 类型的引用，保持与 Sys3C 程序集一起
    /// </summary>
    public class StateCoordinator
    {
        // FSM 类型从 Sys3C 程序集引用
        private readonly object _baseFSM;
        private readonly object _attackFSM;
        private readonly object _hitFSM;

        private LayerType _activeLayer = LayerType.Base;
        private LayerType _lockedLayer = LayerType.Base;
        private float _resistance = 100f;

        public LayerType ActiveLayer => _activeLayer;

        /// <summary>
        /// 是否可以移动：非Hit层时可以移动
        /// </summary>
        public bool CanMove => _activeLayer != LayerType.Hit;

        /// <summary>
        /// 是否可以攻击：非Hit层时可以攻击
        /// </summary>
        public bool CanAttack => _activeLayer != LayerType.Hit;

        /// <summary>
        /// 是否有霸体
        /// </summary>
        public bool HasSuperArmor
        {
            get
            {
                var attackProp = _attackFSM?.GetType().GetProperty("HasSuperArmor");
                var hitProp = _hitFSM?.GetType().GetProperty("HasSuperArmor");
                return (bool)(attackProp?.GetValue(_attackFSM) ?? false) ||
                       (bool)(hitProp?.GetValue(_hitFSM) ?? false);
            }
        }

        /// <summary>
        /// 是否处于免疫状态（死亡）
        /// </summary>
        public bool IsImmune
        {
            get
            {
                var hitProp = _hitFSM?.GetType().GetProperty("HasSuperArmor");
                return (bool)(hitProp?.GetValue(_hitFSM) ?? false);
            }
        }

        public StateCoordinator(object baseFSM, object attackFSM, object hitFSM)
        {
            _baseFSM = baseFSM;
            _attackFSM = attackFSM;
            _hitFSM = hitFSM;
        }

        /// <summary>
        /// 初始化协调器
        /// </summary>
        public void Initialize()
        {
            // 订阅事件使用反射
            var baseOnChanged = _baseFSM?.GetType().GetEvent("OnStateChanged");
            // ... 反射订阅较复杂，暂时不实现
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            // HitFSM 由 FSMManager 更新
        }

        /// <summary>
        /// 请求普通攻击
        /// </summary>
        public bool TryRequestAttack()
        {
            if (_activeLayer == LayerType.Hit) return false;
            if (_activeLayer == LayerType.Attack) return true;

            LockLayer(LayerType.Base);
            SetActiveLayer(LayerType.Attack);

            // 调用 AttackFSM
            var method = _attackFSM?.GetType().GetMethod("RequestNormalAttack");
            method?.Invoke(_attackFSM, null);

            EventBus.Emit(new SkillActivatedEvent(0, "NormalAttack"));
            return true;
        }

        /// <summary>
        /// 请求技能
        /// </summary>
        public bool TryRequestSkill(int skillId, string skillName)
        {
            if (_activeLayer == LayerType.Hit) return false;

            // 检查 CanPlaySkill
            var canPlayProp = _attackFSM?.GetType().GetProperty("CanPlaySkill");
            if (!(bool)(canPlayProp?.GetValue(_attackFSM) ?? false)) return false;

            LockLayer(LayerType.Base);
            SetActiveLayer(LayerType.Attack);

            if (skillName == "SkillQ")
            {
                var method = _attackFSM?.GetType().GetMethod("RequestSkillQ");
                method?.Invoke(_attackFSM, null);
            }
            else if (skillName == "SkillR")
            {
                var method = _attackFSM?.GetType().GetMethod("RequestSkillR");
                // 获取 BaseState
                var baseStateProp = _baseFSM?.GetType().GetProperty("CurrentState");
                var currentState = (int)(baseStateProp?.GetValue(_baseFSM) ?? 0);
                var isAir = currentState == 3 || currentState == 4; // JumpStart or JumpAir
                method?.Invoke(_attackFSM, new object[] { isAir });
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
            if (_activeLayer == LayerType.Attack) return false;

            EventBus.Emit(JumpEvent.Start);
            return true;
        }

        /// <summary>
        /// 处理伤害
        /// </summary>
        public void HandleDamage(DamageEvent damage)
        {
            if (IsImmune) return;

            // 检查霸体
            var armorProp = _attackFSM?.GetType().GetProperty("SuperArmorRemaining");
            if ((float)(armorProp?.GetValue(_attackFSM) ?? 0f) > 0)
            {
                Debug.Log("[StateCoordinator] Damage blocked by super armor");
                return;
            }

            _resistance -= damage.Damage * 0.5f;
            if (_resistance < 0) _resistance = 0;

            EventBus.Emit(damage);
            EventBus.Emit(new HitReceivedEvent());
        }

        /// <summary>
        /// 进入死亡状态
        /// </summary>
        public void HandleDeath()
        {
            SetActiveLayer(LayerType.Hit);

            // 锁定 Base 层
            var lockMethod = _baseFSM?.GetType().GetMethod("LockState");
            lockMethod?.Invoke(_baseFSM, new object[] { 0 });

            // AttackFSM ForceIdle
            var forceIdle = _attackFSM?.GetType().GetMethod("ForceIdle");
            forceIdle?.Invoke(_attackFSM, null);

            LockLayer(LayerType.Base);
        }

        /// <summary>
        /// 复活
        /// </summary>
        public void HandleResurrect()
        {
            _resistance = 100f;
            UnlockAndReturnToBase();
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
            _resistance = Mathf.Min(_resistance + amount, 100f);
        }

        /// <summary>
        /// 获取受击位移
        /// </summary>
        public Vector3 GetKnockbackDisplacement()
        {
            var method = _hitFSM?.GetType().GetMethod("GetKnockbackDisplacement");
            return (Vector3)(method?.Invoke(_hitFSM, null) ?? Vector3.zero);
        }

        /// <summary>
        /// 检查角色是否处于空中受击状态
        /// </summary>
        public bool IsInAirHit()
        {
            var stateProp = _hitFSM?.GetType().GetProperty("CurrentState");
            var state = (int)(stateProp?.GetValue(_hitFSM) ?? 0);
            return state == 3; // Launched
        }

        /// <summary>
        /// 获取当前活跃层的状态描述
        /// </summary>
        public string GetActiveStateDescription()
        {
            var baseState = _baseFSM?.GetType().GetProperty("CurrentState")?.GetValue(_baseFSM)?.ToString() ?? "null";
            var attackState = _attackFSM?.GetType().GetProperty("CurrentState")?.GetValue(_attackFSM)?.ToString() ?? "null";
            var hitState = _hitFSM?.GetType().GetProperty("CurrentState")?.GetValue(_hitFSM)?.ToString() ?? "null";
            return $"[Layer: {_activeLayer}] Base={baseState}, Attack={attackState}, Hit={hitState}";
        }

        /// <summary>
        /// 解锁层并返回 Base
        /// </summary>
        public void UnlockAndReturnToBase()
        {
            _lockedLayer = LayerType.Base;
            SetActiveLayer(LayerType.Base);

            var unlockMethod = _baseFSM?.GetType().GetMethod("Unlock");
            unlockMethod?.Invoke(_baseFSM, new object[] { 0 });

            EventBus.Emit(new LayerUnlockedEvent(LayerType.Base));
        }

        public void SetActiveLayer(LayerType layer)
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
    }
}
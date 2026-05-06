using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Skill;
using CharacterController = Hotfix.GameSystems.Sys3C.Character.CharacterController;
using SkillCoordinatorRuntime = Hotfix.GameSystems.Skills.Runtime.SkillCoordinator;
using SkillInput = global::Hotfix.GameSystems.Skills.Runtime.SkillInput;

namespace Hotfix.GameSystems.Sys3C.Skill
{
    /// <summary>
    /// 技能协调器适配器 - 将技能系统与现有FSM/CharacterController集成
    /// 实现IEffectTarget接口供技能系统使用
    /// </summary>
    public class SkillCoordinatorBridge : IEffectTarget
    {
        private readonly CharacterController _characterController;
        private readonly SkillCoordinatorRuntime _skillCoordinator;
        private readonly SkillRegistry _skillRegistry;

        // Buff系统
        private readonly BuffHandler _buffHandler;

        // 回调
        public event Action<int, float> OnCooldownUpdate;
        public event Action<SkillData> OnSkillActivated;
        public event Action<AttackState> OnAttackStateChanged;

        // IEffectTarget 实现
        IEffectStats IEffectTarget.Stats => _characterController.StatsAdapter;
        IShieldSystem IEffectTarget.ShieldSystem => _characterController.ShieldSystemAdapter;
        IPhysicsSystem IEffectTarget.PhysicsSystem => _characterController.PhysicsSystemAdapter;
        IStatusController IEffectTarget.StatusController => _characterController.StatusControllerAdapter;
        Transform IEffectTarget.transform => _characterController.Transform;

        // 属性
        public SkillCoordinatorRuntime Coordinator => _skillCoordinator;
        public BuffHandler BuffHandler => _buffHandler;
        public bool IsCasting => _skillCoordinator.IsCasting;
        public bool IsSkillActive => _skillCoordinator.IsSkillActive;

        public SkillCoordinatorBridge(
            CharacterController characterController,
            SkillRegistry skillRegistry)
        {
            _characterController = characterController;
            _skillRegistry = skillRegistry;
            _skillCoordinator = new SkillCoordinatorRuntime(this);
            _buffHandler = new BuffHandler(this);

            // 注册所有技能
            RegisterAllSkills();

            // 订阅事件
            _skillCoordinator.OnCooldownUpdate += (skillId, progress) =>
                OnCooldownUpdate?.Invoke(skillId, progress);

            _skillCoordinator.OnSkillActivated += (skillData) =>
                OnSkillActivated?.Invoke(skillData);
        }

        private void RegisterAllSkills()
        {
            if (_skillRegistry == null) return;

            _skillCoordinator.RegisterSkills(_skillRegistry.GetAllSkills());
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            _skillCoordinator.Update(deltaTime);
            _buffHandler.Update(deltaTime);
        }

        /// <summary>
        /// 处理普通攻击输入
        /// </summary>
        public void HandleAttackInput(Vector3 direction)
        {
            var input = SkillInput.BasicAttack(GetBasicAttackSkillId(), direction);
            _skillCoordinator.HandleBasicAttackInput(input);
        }

        /// <summary>
        /// 处理技能Q输入
        /// </summary>
        public void HandleSkillQInput(Vector3 targetPosition)
        {
            var input = SkillInput.SkillToPosition(GetSkillQId(), targetPosition);
            _skillCoordinator.HandleInput(input);
        }

        /// <summary>
        /// 处理技能R输入
        /// </summary>
        public void HandleSkillRInput(Vector3 targetPosition)
        {
            var input = SkillInput.SkillToPosition(GetSkillRId(), targetPosition);
            _skillCoordinator.HandleInput(input);
        }

        /// <summary>
        /// 处理受伤 - 中断当前技能
        /// </summary>
        public void HandleDamageTaken(float damage, DamageType damageType)
        {
            _skillCoordinator.OnDamageTaken(damage, damageType);
        }

        /// <summary>
        /// 通知技能完成
        /// </summary>
        public void NotifySkillCompleted(int skillId)
        {
            // 更新AttackFSM状态
            OnAttackStateChanged?.Invoke(AttackState.Idle);
        }

        /// <summary>
        /// 通知攻击完成
        /// </summary>
        public void NotifyAttackCompleted()
        {
            // 更新AttackFSM状态
            OnAttackStateChanged?.Invoke(AttackState.Idle);
        }

        /// <summary>
        /// 获取当前技能状态
        /// </summary>
        public SkillSubState GetCurrentSkillSubState()
        {
            return _skillCoordinator.CurrentSubState;
        }

        /// <summary>
        /// 是否可以移动
        /// </summary>
        public bool CanMove()
        {
            return _skillCoordinator.CanMove();
        }

        /// <summary>
        /// 是否可以旋转
        /// </summary>
        public bool CanRotate()
        {
            return _skillCoordinator.CanRotate();
        }

        /// <summary>
        /// 应用伤害（实现IEffectTarget接口）
        /// </summary>
        public void ApplyDamageTo(IEffectTarget target, float damage, DamageType damageType)
        {
            // TODO: 调用战斗系统处理伤害
        }

        /// <summary>
        /// 治疗（实现IEffectTarget接口）
        /// </summary>
        public void Heal(float amount)
        {
            // TODO: 修改角色生命值
        }

        private int GetBasicAttackSkillId()
        {
            return _skillRegistry?.GetBasicAttack1Id() ?? 10001;
        }

        private int GetSkillQId()
        {
            return _skillRegistry?.GetSkillQId() ?? 20001;
        }

        private int GetSkillRId()
        {
            return _skillRegistry?.GetSkillRId() ?? 20002;
        }
    }

    /// <summary>
    /// Buff处理器 - 管理角色身上的Buff/Debuff
    /// </summary>
    public class BuffHandler
    {
        private readonly SkillCoordinatorBridge _owner;
        private readonly Dictionary<string, ActiveBuff> _activeBuffs = new();
        private readonly List<string> _buffsToRemove = new();

        public event Action<BuffData, SkillCoordinatorBridge> OnBuffApplied;
        public event Action<BuffData, SkillCoordinatorBridge> OnBuffRemoved;
        public event Action<string, int> OnStackChanged;

        public BuffHandler(SkillCoordinatorBridge owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// 应用Buff
        /// </summary>
        public void ApplyBuff(BuffData data, SkillCoordinatorBridge caster)
        {
            if (_activeBuffs.TryGetValue(data.BuffId, out var existing))
            {
                switch (data.StackingRule)
                {
                    case StackingRule.Refresh:
                        existing.Refresh();
                        break;
                    case StackingRule.Stack:
                        existing.AddStack();
                        OnStackChanged?.Invoke(data.BuffId, existing.CurrentStacks);
                        break;
                    case StackingRule.Ignore:
                        return;
                }
            }
            else
            {
                var newBuff = new ActiveBuff(data, caster, _owner);
                _activeBuffs[data.BuffId] = newBuff;
                data.Effect.Apply(caster, _owner);
                OnBuffApplied?.Invoke(data, _owner);
            }
        }

        /// <summary>
        /// 移除Buff
        /// </summary>
        public void RemoveBuff(string buffId)
        {
            if (_activeBuffs.TryGetValue(buffId, out var buff))
            {
                buff.Remove();
                _activeBuffs.Remove(buffId);
                OnBuffRemoved?.Invoke(buff.Data, _owner);
            }
        }

        /// <summary>
        /// 是否有特定Buff
        /// </summary>
        public bool HasBuff(string buffId) => _activeBuffs.ContainsKey(buffId);

        /// <summary>
        /// 获取Buff层数
        /// </summary>
        public int GetStackCount(string buffId) =>
            _activeBuffs.TryGetValue(buffId, out var buff) ? buff.CurrentStacks : 0;

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            _buffsToRemove.Clear();

            foreach (var kvp in _activeBuffs)
            {
                kvp.Value.Update(deltaTime);
                if (kvp.Value.IsExpired)
                {
                    _buffsToRemove.Add(kvp.Key);
                }
            }

            foreach (var buffId in _buffsToRemove)
            {
                RemoveBuff(buffId);
            }
        }

        /// <summary>
        /// 清除所有Buff
        /// </summary>
        public void ClearAll()
        {
            foreach (var kvp in _activeBuffs)
            {
                kvp.Value.Remove();
            }
            _activeBuffs.Clear();
        }

        /// <summary>
        /// 是否有控制效果
        /// </summary>
        public bool HasControlEffect()
        {
            foreach (var kvp in _activeBuffs)
            {
                if (kvp.Value.Data.IsControlEffect)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 活跃的Buff实例
    /// </summary>
    public class ActiveBuff
    {
        public BuffData Data { get; }
        public SkillCoordinatorBridge Caster { get; }
        public int CurrentStacks { get; private set; }
        public bool IsExpired => _remainingTime <= 0;

        private readonly SkillCoordinatorBridge _owner;
        private float _remainingTime;
        private float _tickTimer;

        public ActiveBuff(BuffData data, SkillCoordinatorBridge caster, SkillCoordinatorBridge owner)
        {
            Data = data;
            Caster = caster;
            _owner = owner;
            _remainingTime = data.Duration;
            CurrentStacks = 1;
            data.Effect.Apply(caster, owner);
        }

        public void Refresh()
        {
            _remainingTime = Data.Duration;
        }

        public void AddStack()
        {
            if (CurrentStacks < Data.MaxStacks)
            {
                CurrentStacks++;
            }
            else
            {
                _remainingTime = Data.Duration;
            }
        }

        public void Update(float deltaTime)
        {
            _remainingTime -= deltaTime;

            if (Data.Effect.IsTickEffect && Data.Effect.TickInterval > 0)
            {
                _tickTimer += deltaTime;
                if (_tickTimer >= Data.Effect.TickInterval)
                {
                    _tickTimer = 0;
                    Data.Effect.OnTick(Caster, _owner);
                }
            }
        }

        public void Remove()
        {
            Data.Effect.Remove(Caster, _owner);
        }
    }
}
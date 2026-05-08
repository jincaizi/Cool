using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 技能执行器 - 管理单个技能的完整生命周期
    /// </summary>
    public class SkillExecutor
    {
        private readonly IEffectTarget _owner;
        private readonly SkillData _skillData;
        private readonly SkillStateMachine _stateMachine;
        private readonly SkillInterruptionMatrix _interruptionMatrix;

        // 目标检测
        private Vector3 _targetPosition;
        private IEffectTarget _targetCharacter;
        private IDashComponent _dashComponent;

        // 回调
        public event Action<int> OnHitboxFrame;              // 判定帧触发
        public event Action OnSkillCompleted;
        public event Action<InterruptionSource> OnSkillInterrupted;
        public event Action<IEffectTarget> OnTargetHit;

        public SkillData Data => _skillData;
        public int SkillId => _skillData.SkillId;
        public SkillSubState CurrentSubState => _stateMachine.CurrentState;
        public bool IsActive => _stateMachine.CurrentState != SkillSubState.Completed &&
                                _stateMachine.CurrentState != SkillSubState.Cancelled &&
                                _stateMachine.CurrentState != SkillSubState.Cooldown;
        public IEffectTarget Owner => _owner;
        public float ElapsedTime => _stateMachine.ElapsedTime;

        public SkillExecutor(
            IEffectTarget owner,
            SkillData data,
            SkillInterruptionMatrix interruptionMatrix = null)
        {
            _owner = owner;
            _skillData = data;
            _interruptionMatrix = interruptionMatrix ?? new SkillInterruptionMatrix();
            _stateMachine = new SkillStateMachine(data);

            _stateMachine.OnHitboxFrame += OnHitboxTriggered;
            _stateMachine.OnHitConfirm += OnHitConfirm;
            _stateMachine.OnSkillCompleted += OnSkillComplete;
            _stateMachine.OnSkillInterrupted += OnSkillInterrupt;
            _stateMachine.OnStateChanged += OnStateChanged;
        }

        /// <summary>
        /// 设置技能目标
        /// </summary>
        public void SetTarget(IEffectTarget target)
        {
            _targetCharacter = target;
            if (target != null)
            {
                _targetPosition = target.transform.position;
            }
        }

        /// <summary>
        /// 设置目标位置
        /// </summary>
        public void SetTargetPosition(Vector3 position)
        {
            _targetPosition = position;
        }

        public void SetDashComponent(IDashComponent dashComponent)
        {
            _dashComponent = dashComponent;
        }

        /// <summary>
        /// 尝试开始释放技能
        /// </summary>
        public bool TryStart()
        {
            return _stateMachine.TryStart();
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            _stateMachine.Update(deltaTime);
        }

        /// <summary>
        /// 释放蓄力（松开蓄力键）
        /// </summary>
        public void ReleaseCharge()
        {
            if (CurrentSubState == SkillSubState.Charging)
            {
                _stateMachine.ReleaseCharge();
            }
        }

        /// <summary>
        /// 尝试中断技能
        /// </summary>
        public bool TryInterrupt(InterruptionSource source)
        {
            if (_interruptionMatrix.CanBeInterruptedInState(_skillData, CurrentSubState, source))
            {
                _stateMachine.Interrupt(source);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 强制完成技能
        /// </summary>
        public void ForceComplete()
        {
            _stateMachine.Complete();
        }

        /// <summary>
        /// 获取蓄力进度 [0, 1]
        /// </summary>
        public float GetChargeProgress()
        {
            if (CurrentSubState != SkillSubState.Charging)
                return CurrentSubState == SkillSubState.Completed ? 1f : 0f;

            var charged = _skillData as ChargedSkillData;
            if (charged == null) return 1f;
            return Mathf.Clamp01(_stateMachine.ElapsedTime / charged.MaxChargeTime);
        }

        /// <summary>
        /// 获取引导进度 [0, 1]
        /// </summary>
        public float GetChannelProgress()
        {
            if (CurrentSubState != SkillSubState.Channeling)
                return CurrentSubState == SkillSubState.Completed ? 1f : 0f;

            var channeled = _skillData as ChanneledSkillData;
            if (channeled == null) return 1f;
            float elapsed = _stateMachine.ElapsedTime - channeled.CastTime;
            return Mathf.Clamp01(elapsed / channeled.ChannelDuration);
        }

        /// <summary>
        /// 获取读条进度 [0, 1]
        /// </summary>
        public float GetCastProgress()
        {
            if (CurrentSubState != SkillSubState.Casting)
                return CurrentSubState == SkillSubState.Completed ? 1f : 0f;

            var channeled = _skillData as ChanneledSkillData;
            float castTime = channeled?.CastTime ?? 0f;
            if (castTime <= 0) return 1f;
            return Mathf.Clamp01(_stateMachine.ElapsedTime / castTime);
        }

        private void OnHitboxTriggered(int frameIndex)
        {
            // 检测目标
            var targets = DetectTargets();

            foreach (var target in targets)
            {
                // 应用伤害
                ApplyDamage(target, frameIndex);

                // 应用效果
                ApplyEffects(target);

                // 触发命中回调
                OnTargetHit?.Invoke(target);
            }

            // 触发判定帧事件
            OnHitboxFrame?.Invoke(frameIndex);
        }

        private void OnHitConfirm()
        {
            // 可以在这里触发命中特效、音效等
            PlayHitEffects();
        }

        private void OnSkillComplete()
        {
            OnSkillCompleted?.Invoke();
        }

        private void OnSkillInterrupt(InterruptionSource source)
        {
            OnSkillInterrupted?.Invoke(source);
        }

        private void OnStateChanged(SkillSubState newState)
        {
            if (newState == SkillSubState.Execution &&
                _dashComponent != null &&
                _skillData.DashDistance > 0)
            {
                Vector3 dashDir = _owner.transform.forward;
                _dashComponent.StartDash(dashDir, _skillData.DashDistance, _skillData.DashDuration);
            }
        }

        private ShapeBlock GetShape()
        {
            return (_skillData as ComboSkillData)?.Shape
                ?? (_skillData as InstantSkillData)?.Shape
                ?? (_skillData as ChargedSkillData)?.Shape
                ?? (_skillData as ChanneledSkillData)?.Shape;
        }

        private List<IEffectTarget> DetectTargets()
        {
            var targets = new List<IEffectTarget>();
            ShapeBlock shape = GetShape();
            if (shape == null) return targets;

            if (shape.AreaRadius > 0)
                DetectAOETargets(targets, shape);
            else
                DetectSingleTarget(targets, shape);
            return targets;
        }

        private void DetectSingleTarget(List<IEffectTarget> targets, ShapeBlock shape)
        {
            if (_targetCharacter != null && _targetCharacter != _owner)
            {
                float distance = Vector3.Distance(_owner.transform.position, _targetCharacter.transform.position);
                if (distance <= shape.Range)
                    targets.Add(_targetCharacter);
            }
            else
            {
                Ray ray = new Ray(_owner.transform.position, _owner.transform.forward);
                if (Physics.Raycast(ray, out var hit, shape.Range, shape.TargetMask))
                {
                    if (hit.collider.TryGetComponent(out IEffectTarget target) && target != _owner)
                        targets.Add(target);
                }
            }
        }

        private void DetectAOETargets(List<IEffectTarget> targets, ShapeBlock shape)
        {
            Vector3 center = _targetCharacter != null
                ? _targetCharacter.transform.position : _targetPosition;

            if (shape.TargetType == TargetType.AOE_Cone)
                DetectConeTargets(center, targets, shape);
            else
            {
                var colliders = Physics.OverlapSphere(center, shape.AreaRadius, shape.TargetMask);
                foreach (var collider in colliders)
                {
                    if (collider.TryGetComponent(out IEffectTarget target) && target != _owner)
                        targets.Add(target);
                }
            }
        }

        private void DetectConeTargets(Vector3 center, List<IEffectTarget> targets, ShapeBlock shape)
        {
            Vector3 ownerPos = _owner.transform.position;
            Vector3 directionToCenter = (center - ownerPos).normalized;
            float halfAngle = shape.Angle / 2f;
            var colliders = Physics.OverlapSphere(ownerPos, shape.Range, shape.TargetMask);
            foreach (var collider in colliders)
            {
                if (collider.TryGetComponent(out IEffectTarget target) && target != _owner)
                {
                    Vector3 dirToTarget = (target.transform.position - ownerPos).normalized;
                    float angle = Vector3.Angle(directionToCenter, dirToTarget);
                    if (angle <= halfAngle)
                        targets.Add(target);
                }
            }
        }

        private void ApplyDamage(IEffectTarget target, int frameIndex)
        {
            var damageBlock = _skillData.Damage;
            if (damageBlock == null) return;

            float damage = damageBlock.CalculateFinalDamage(_owner.Stats);

            if (CurrentSubState == SkillSubState.Charging || CurrentSubState == SkillSubState.Execution)
                damage *= 1f + GetChargeProgress() * 0.5f;

            target.Heal(-damage);
        }

        private void ApplyEffects(IEffectTarget target)
        {
            EffectBlock effect = (_skillData as InstantSkillData)?.Effect
                ?? (_skillData as ChargedSkillData)?.Effect
                ?? (_skillData as ChanneledSkillData)?.Effect
                ?? (_skillData as ProjectileSkillData)?.Effect;

            if (effect?.ApplyEffects == null) return;
            foreach (var effectData in effect.ApplyEffects)
                effectData?.Apply(_owner, target);
        }

        private void PlayHitEffects()
        {
            PresentationBlock pres = GetPresentation();
            if (pres?.ReleaseVFX != null)
                UnityEngine.Object.Instantiate(pres.ReleaseVFX, _targetPosition, Quaternion.identity);
        }

        private PresentationBlock GetPresentation()
        {
            return (_skillData as ComboSkillData)?.Presentation
                ?? (_skillData as InstantSkillData)?.Presentation
                ?? (_skillData as ChargedSkillData)?.Presentation
                ?? (_skillData as ChanneledSkillData)?.Presentation
                ?? (_skillData as ProjectileSkillData)?.Presentation;
        }
    }
}
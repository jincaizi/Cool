using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Skill;
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
        private SkillDashComponent _dashComponent;

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

        public void SetDashComponent(SkillDashComponent dashComponent)
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
            {
                return CurrentSubState == SkillSubState.Completed ? 1f : 0f;
            }

            float chargeTime = _stateMachine.ElapsedTime;
            float maxTime = _skillData.MaxChargeTime;
            return Mathf.Clamp01(chargeTime / maxTime);
        }

        /// <summary>
        /// 获取引导进度 [0, 1]
        /// </summary>
        public float GetChannelProgress()
        {
            if (CurrentSubState != SkillSubState.Channeling)
            {
                return CurrentSubState == SkillSubState.Completed ? 1f : 0f;
            }

            float elapsed = _stateMachine.ElapsedTime - _skillData.CastTime;
            return Mathf.Clamp01(elapsed / _skillData.ChannelDuration);
        }

        /// <summary>
        /// 获取读条进度 [0, 1]
        /// </summary>
        public float GetCastProgress()
        {
            if (CurrentSubState != SkillSubState.Casting)
            {
                return CurrentSubState == SkillSubState.Completed ? 1f : 0f;
            }

            return Mathf.Clamp01(_stateMachine.ElapsedTime / _skillData.CastTime);
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

        private List<IEffectTarget> DetectTargets()
        {
            var targets = new List<IEffectTarget>();

            if (_skillData.AreaRadius > 0)
            {
                // AOE检测
                DetectAOETargets(targets);
            }
            else
            {
                // 单体检测
                DetectSingleTarget(targets);
            }

            return targets;
        }

        private void DetectAOETargets(List<IEffectTarget> targets)
        {
            Vector3 center = _targetPosition;
            if (_targetCharacter != null)
            {
                center = _targetCharacter.transform.position;
            }

            // 扇形检测
            if (_skillData.Angle < 360f)
            {
                DetectConeTargets(center, targets);
            }
            else
            {
                // 圆形检测
                var colliders = Physics.OverlapSphere(center, _skillData.AreaRadius, _skillData.TargetMask);
                foreach (var collider in colliders)
                {
                    if (collider.TryGetComponent(out IEffectTarget target) && target != _owner)
                    {
                        targets.Add(target);
                    }
                }
            }
        }

        private void DetectConeTargets(Vector3 center, List<IEffectTarget> targets)
        {
            Vector3 ownerPos = _owner.transform.position;
            Vector3 directionToCenter = (center - ownerPos).normalized;
            float halfAngle = _skillData.Angle / 2f;

            var colliders = Physics.OverlapSphere(ownerPos, _skillData.Range, _skillData.TargetMask);
            foreach (var collider in colliders)
            {
                if (collider.TryGetComponent(out IEffectTarget target) && target != _owner)
                {
                    Vector3 dirToTarget = (target.transform.position - ownerPos).normalized;
                    float angle = Vector3.Angle(directionToCenter, dirToTarget);

                    if (angle <= halfAngle)
                    {
                        targets.Add(target);
                    }
                }
            }
        }

        private void DetectSingleTarget(List<IEffectTarget> targets)
        {
            if (_targetCharacter != null && _targetCharacter != _owner)
            {
                // 检查距离
                float distance = Vector3.Distance(_owner.transform.position, _targetCharacter.transform.position);
                if (distance <= _skillData.Range)
                {
                    targets.Add(_targetCharacter);
                }
            }
            else
            {
                // 朝前方射线检测
                Ray ray = new Ray(_owner.transform.position, _owner.transform.forward);
                if (Physics.Raycast(ray, out var hit, _skillData.Range, _skillData.TargetMask))
                {
                    if (hit.collider.TryGetComponent(out IEffectTarget target) && target != _owner)
                    {
                        targets.Add(target);
                    }
                }
            }
        }

        private void ApplyDamage(IEffectTarget target, int frameIndex)
        {
            if (_skillData.DamageData == null) return;

            // 计算伤害
            float damage = _skillData.DamageData.CalculateFinalDamage(_owner.Stats);

            // 蓄力缩放
            if (CurrentSubState == SkillSubState.Charging || CurrentSubState == SkillSubState.Execution)
            {
                float chargeScale = 1f + GetChargeProgress() * 0.5f; // 蓄力增加50%伤害
                damage *= chargeScale;
            }

            // 应用伤害 - 通过接口或直接调用目标方法
            // 这里需要伤害系统支持，暂时通过接口
            target.Heal(-damage); // 简化：直接调用Heal作为伤害
        }

        private void ApplyEffects(IEffectTarget target)
        {
            if (_skillData.ApplyEffects?.Effects == null) return;

            foreach (var effectData in _skillData.ApplyEffects.Effects)
            {
                if (effectData != null)
                {
                    effectData.Apply(_owner, target);
                }
            }
        }

        private void PlayHitEffects()
        {
            // 播放命中特效
            if (_skillData.ReleaseVFX != null)
            {
                // 实例化特效
                UnityEngine.Object.Instantiate(_skillData.ReleaseVFX, _targetPosition, Quaternion.identity);
            }

            // 播放命中音效
            if (_skillData.CastSFX != null)
            {
                // AudioSource.PlayClipAtPoint(_skillData.CastSFX, _targetPosition);
            }
        }
    }
}
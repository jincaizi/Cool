using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 技能状态机 - 管理单个技能的子状态转换
    /// </summary>
    public class SkillStateMachine
    {
        private readonly SkillData _skillData;
        private SkillSubState _currentState;
        private float _stateStartTime;
        private float _elapsedTime;

        // 蓄力相关
        private float _chargeStartTime;
        private bool _isCharging;

        // 引导相关
        private int _currentTick;
        private float _lastTickTime;
        private int _totalChannelTicks;

        // 回调
        private event Action<SkillSubState> _onStateChanged;
        private event Action<int> _onHitboxFrame;
        private event Action _onHitConfirm;
        private event Action _onSkillCompleted;
        private event Action<InterruptionSource> _onSkillInterrupted;

        public SkillSubState CurrentState => _currentState;
        public float ElapsedTime => _elapsedTime;
        public float NormalizedTime => _skillData.GetTotalDuration() > 0
            ? _elapsedTime / _skillData.GetTotalDuration()
            : 0f;
        public float StateDuration => _skillData.GetTotalDuration();

        public event Action<SkillSubState> OnStateChanged
        {
            add => _onStateChanged += value;
            remove => _onStateChanged -= value;
        }

        public event Action<int> OnHitboxFrame
        {
            add => _onHitboxFrame += value;
            remove => _onHitboxFrame -= value;
        }

        public event Action OnHitConfirm
        {
            add => _onHitConfirm += value;
            remove => _onHitConfirm -= value;
        }

        public event Action OnSkillCompleted
        {
            add => _onSkillCompleted += value;
            remove => _onSkillCompleted -= value;
        }

        public event Action<InterruptionSource> OnSkillInterrupted
        {
            add => _onSkillInterrupted += value;
            remove => _onSkillInterrupted -= value;
        }

        public SkillStateMachine(SkillData data, Action<SkillSubState> onStateChanged = null)
        {
            _skillData = data;
            _onStateChanged = onStateChanged;
            _currentState = SkillSubState.Ready;
            _stateStartTime = -1f;

            // 计算引导总tick数
            if (data.ChannelDuration > 0 && data.HitboxTimings != null && data.HitboxTimings.Length > 0)
            {
                _totalChannelTicks = data.HitboxTimings.Length;
            }
        }

        /// <summary>
        /// 开始准备释放技能
        /// </summary>
        public bool TryStart()
        {
            if (_currentState != SkillSubState.Ready && _currentState != SkillSubState.Cooldown)
            {
                return false;
            }

            switch (_skillData.ReleaseType)
            {
                case ReleaseType.Instant:
                    TransitionTo(SkillSubState.Execution);
                    break;

                case ReleaseType.Timed:
                    TransitionTo(SkillSubState.Casting);
                    break;

                case ReleaseType.Charged:
                    TransitionTo(SkillSubState.Casting);
                    _isCharging = true;
                    _chargeStartTime = GetCurrentTime();
                    break;

                case ReleaseType.Channeled:
                    TransitionTo(SkillSubState.Casting);
                    break;
            }

            return true;
        }

        /// <summary>
        /// 更新状态机（每帧调用）
        /// </summary>
        public void Update(float deltaTime)
        {
            if (_stateStartTime < 0) return;

            _elapsedTime = GetCurrentTime() - _stateStartTime;

            switch (_currentState)
            {
                case SkillSubState.Casting:
                    UpdateCasting();
                    break;

                case SkillSubState.Charging:
                    UpdateCharging();
                    break;

                case SkillSubState.Channeling:
                    UpdateChanneling();
                    break;

                case SkillSubState.Execution:
                    UpdateExecution();
                    break;

                case SkillSubState.Recovery:
                    UpdateRecovery();
                    break;
            }
        }

        /// <summary>
        /// 打断技能
        /// </summary>
        public bool Interrupt(InterruptionSource source)
        {
            // 检查是否可被打断
            if (!CanBeInterrupted(source))
            {
                return false;
            }

            TransitionTo(SkillSubState.Cancelled);
            _onSkillInterrupted?.Invoke(source);
            return true;
        }

        /// <summary>
        /// 完成技能（正常结束）
        /// </summary>
        public void Complete()
        {
            TransitionTo(SkillSubState.Completed);
            _onSkillCompleted?.Invoke();
        }

        /// <summary>
        /// 设置冷却状态
        /// </summary>
        public void SetCooldown()
        {
            if (_currentState == SkillSubState.Ready)
            {
                TransitionTo(SkillSubState.Cooldown);
            }
        }

        /// <summary>
        /// 冷却结束，进入就绪状态
        /// </summary>
        public void SetReady()
        {
            if (_currentState == SkillSubState.Cooldown)
            {
                TransitionTo(SkillSubState.Ready);
            }
        }

        /// <summary>
        /// 获取技能数据
        /// </summary>
        public SkillData GetSkillData() => _skillData;

        private void UpdateCasting()
        {
            if (_elapsedTime >= _skillData.CastTime)
            {
                switch (_skillData.ReleaseType)
                {
                    case ReleaseType.Timed:
                        TransitionTo(SkillSubState.Execution);
                        break;

                    case ReleaseType.Charged:
                        TransitionTo(SkillSubState.Charging);
                        _chargeStartTime = GetCurrentTime();
                        break;

                    case ReleaseType.Channeled:
                        TransitionTo(SkillSubState.Channeling);
                        _currentTick = 0;
                        _lastTickTime = GetCurrentTime();
                        break;
                }
            }
        }

        private void UpdateCharging()
        {
            float chargeTime = GetCurrentTime() - _chargeStartTime;

            // 蓄力完成或松开
            if (chargeTime >= _skillData.MaxChargeTime)
            {
                TransitionTo(SkillSubState.Execution);
            }
            else if (!_isCharging && chargeTime >= _skillData.MinChargeTime)
            {
                // 松开蓄力键
                TransitionTo(SkillSubState.Execution);
            }
        }

        /// <summary>
        /// 释放蓄力键
        /// </summary>
        public void ReleaseCharge()
        {
            if (_currentState == SkillSubState.Charging)
            {
                _isCharging = false;
            }
        }

        private void UpdateChanneling()
        {
            if (_skillData.HitboxTimings == null || _skillData.HitboxTimings.Length == 0)
            {
                // 无判定帧，持续到结束
                if (_elapsedTime >= _skillData.ChannelDuration)
                {
                    TransitionTo(SkillSubState.Execution);
                }
                return;
            }

            // 处理引导伤害tick
            for (int i = _currentTick; i < _skillData.HitboxTimings.Length; i++)
            {
                float tickTime = _skillData.CastTime + _skillData.HitboxTimings[i];
                if (_elapsedTime >= tickTime)
                {
                    _currentTick = i + 1;
                    _onHitboxFrame?.Invoke(i);
                    _onHitConfirm?.Invoke();
                }
            }

            // 引导结束
            if (_elapsedTime >= _skillData.ChannelDuration)
            {
                TransitionTo(SkillSubState.Execution);
            }
        }

        private void UpdateExecution()
        {
            // 处理判定帧
            if (_skillData.HitboxTimings != null)
            {
                for (int i = 0; i < _skillData.HitboxTimings.Length; i++)
                {
                    float hitboxTime = _skillData.CastTime + _skillData.HitboxTimings[i];
                    if (Approximately(_elapsedTime, hitboxTime) ||
                        (_elapsedTime > hitboxTime && _elapsedTime < hitboxTime + 0.05f))
                    {
                        _onHitboxFrame?.Invoke(i);
                        _onHitConfirm?.Invoke();
                    }
                }
            }

            // 执行时间结束，进入收招
            float executionDuration = GetExecutionDuration();
            if (executionDuration > 0 && _elapsedTime >= _skillData.CastTime + executionDuration)
            {
                TransitionTo(SkillSubState.Recovery);
            }
            else if (executionDuration <= 0)
            {
                // 没有明确执行时长，立即进入收招
                TransitionTo(SkillSubState.Recovery);
            }
        }

        private void UpdateRecovery()
        {
            float recoveryDuration = GetRecoveryDuration();
            if (recoveryDuration <= 0)
            {
                // 无收招时间，直接完成
                Complete();
                return;
            }

            float recoveryStartTime = _skillData.CastTime + GetExecutionDuration();
            if (_elapsedTime >= recoveryStartTime + recoveryDuration)
            {
                Complete();
            }
        }

        private void TransitionTo(SkillSubState newState)
        {
            var prevState = _currentState;
            _currentState = newState;
            _stateStartTime = GetCurrentTime();
            _elapsedTime = 0f;

            _onStateChanged?.Invoke(newState);
        }

        private bool CanBeInterrupted(InterruptionSource source)
        {
            switch (source)
            {
                case InterruptionSource.DamageTaken:
                    return _skillData.CanBeInterruptedByDamage;

                case InterruptionSource.MovementInput:
                    return _skillData.CanBeInterruptedByMovement;

                case InterruptionSource.Stun:
                    // 眩晕无法被抵抗
                    return true;

                case InterruptionSource.RollDodge:
                case InterruptionSource.Parry:
                    // 翻滚和招架总是可以触发
                    return true;

                default:
                    return false;
            }
        }

        private float GetCurrentTime() => Time.time;

        private float GetExecutionDuration()
        {
            // 从动画Clip获取，或使用默认值
            var clip = _skillData.ReleaseClip ?? _skillData.GetMainAnimationClip();
            return clip != null ? clip.length : 0.5f;
        }

        private float GetRecoveryDuration()
        {
            // 收招时间可以从配置获取，这里使用简化处理
            return 0.1f;
        }

        private bool Approximately(float a, float b) => Mathf.Approximately(a, b);
    }
}
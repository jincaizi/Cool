using System;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;

namespace Hotfix.GameSystems.Skills.Runtime
{
    public class SkillStateMachine
    {
        private readonly SkillData _skillData;
        private SkillSubState _currentState;
        private float _stateStartTime;
        private float _elapsedTime;
        private float _chargeStartTime;
        private bool _isCharging;
        private int _currentTick;
        private float _lastTickTime;

        // Cached type checks
        private readonly bool _isCharged;
        private readonly bool _isChanneled;
        private readonly ChargedSkillData _chargedData;
        private readonly ChanneledSkillData _channeledData;

        private event Action<SkillSubState> _onStateChanged;
        private event Action<int> _onHitboxFrame;
        private event Action _onHitConfirm;
        private event Action _onSkillCompleted;
        private event Action<InterruptionSource> _onSkillInterrupted;

        public SkillSubState CurrentState => _currentState;
        public float ElapsedTime => _elapsedTime;

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

        public SkillStateMachine(SkillData data)
        {
            _skillData = data;
            _chargedData = data as ChargedSkillData;
            _channeledData = data as ChanneledSkillData;
            _isCharged = _chargedData != null;
            _isChanneled = _channeledData != null;
            _currentState = SkillSubState.Ready;
            _stateStartTime = -1f;
        }

        public bool TryStart()
        {
            if (_currentState != SkillSubState.Ready && _currentState != SkillSubState.Cooldown)
                return false;

            if (_isCharged)
            {
                TransitionTo(SkillSubState.Casting);
                _isCharging = true;
                _chargeStartTime = GetCurrentTime();
            }
            else if (_isChanneled)
            {
                TransitionTo(SkillSubState.Casting);
            }
            else
            {
                // Instant, Combo, Projectile — skip cast
                TransitionTo(SkillSubState.Execution);
            }
            return true;
        }

        public void Update(float deltaTime)
        {
            if (_stateStartTime < 0) return;
            _elapsedTime = GetCurrentTime() - _stateStartTime;

            switch (_currentState)
            {
                case SkillSubState.Casting: UpdateCasting(); break;
                case SkillSubState.Charging: UpdateCharging(); break;
                case SkillSubState.Channeling: UpdateChanneling(); break;
                case SkillSubState.Execution: UpdateExecution(); break;
                case SkillSubState.Recovery: UpdateRecovery(); break;
            }
        }

        public void ReleaseCharge()
        {
            if (_currentState == SkillSubState.Charging)
                _isCharging = false;
        }

        public bool Interrupt(InterruptionSource source)
        {
            if (!CanBeInterrupted(source)) return false;
            TransitionTo(SkillSubState.Cancelled);
            _onSkillInterrupted?.Invoke(source);
            return true;
        }

        public void Complete()
        {
            TransitionTo(SkillSubState.Completed);
            _onSkillCompleted?.Invoke();
        }

        private void UpdateCasting()
        {
            float castTime = _channeledData?.CastTime ?? (_chargedData != null ? 0.1f : 0f);
            if (_elapsedTime >= castTime)
            {
                if (_isCharged)
                {
                    TransitionTo(SkillSubState.Charging);
                    _chargeStartTime = GetCurrentTime();
                }
                else if (_isChanneled)
                {
                    TransitionTo(SkillSubState.Channeling);
                    _currentTick = 0;
                    _lastTickTime = GetCurrentTime();
                }
                else
                {
                    TransitionTo(SkillSubState.Execution);
                }
            }
        }

        private void UpdateCharging()
        {
            if (_chargedData == null) { Complete(); return; }

            float chargeTime = GetCurrentTime() - _chargeStartTime;
            if (chargeTime >= _chargedData.MaxChargeTime)
                TransitionTo(SkillSubState.Execution);
            else if (!_isCharging && chargeTime >= _chargedData.MinChargeTime)
                TransitionTo(SkillSubState.Execution);
        }

        private void UpdateChanneling()
        {
            if (_channeledData == null) { Complete(); return; }

            float[] hitboxTimings = GetHitboxTimings();
            if (hitboxTimings == null || hitboxTimings.Length == 0)
            {
                if (_elapsedTime >= _channeledData.ChannelDuration)
                    TransitionTo(SkillSubState.Execution);
                return;
            }

            for (int i = _currentTick; i < hitboxTimings.Length; i++)
            {
                float tickTime = _channeledData.CastTime + hitboxTimings[i];
                if (_elapsedTime >= tickTime)
                {
                    _currentTick = i + 1;
                    _onHitboxFrame?.Invoke(i);
                    _onHitConfirm?.Invoke();
                }
            }

            if (_elapsedTime >= _channeledData.ChannelDuration)
                TransitionTo(SkillSubState.Execution);
        }

        private void UpdateExecution()
        {
            float[] hitboxTimings = GetHitboxTimings();
            if (hitboxTimings != null)
            {
                float castTime = _channeledData?.CastTime ?? 0f;
                for (int i = 0; i < hitboxTimings.Length; i++)
                {
                    float hitboxTime = castTime + hitboxTimings[i];
                    if (Approximately(_elapsedTime, hitboxTime) ||
                        (_elapsedTime > hitboxTime && _elapsedTime < hitboxTime + 0.05f))
                    {
                        _onHitboxFrame?.Invoke(i);
                        _onHitConfirm?.Invoke();
                    }
                }
            }

            float executionDuration = GetExecutionDuration();
            if (executionDuration > 0)
            {
                float castTime = _channeledData?.CastTime ?? 0f;
                if (_elapsedTime >= castTime + executionDuration)
                    TransitionTo(SkillSubState.Recovery);
            }
            else
            {
                TransitionTo(SkillSubState.Recovery);
            }
        }

        private void UpdateRecovery()
        {
            float recoveryDuration = 0.1f;
            float castTime = _channeledData?.CastTime ?? 0f;
            float recoveryStartTime = castTime + GetExecutionDuration();
            if (_elapsedTime >= recoveryStartTime + recoveryDuration)
                Complete();
        }

        private float[] GetHitboxTimings()
        {
            return (_skillData as ComboSkillData)?.Shape?.HitboxTimings
                ?? (_skillData as InstantSkillData)?.Shape?.HitboxTimings
                ?? (_skillData as ChargedSkillData)?.Shape?.HitboxTimings
                ?? (_skillData as ChanneledSkillData)?.Shape?.HitboxTimings;
        }

        private float GetExecutionDuration()
        {
            var clip = _skillData.ReleaseClip ?? _skillData.GetMainAnimationClip();
            return clip != null ? clip.length : 0.5f;
        }

        private void TransitionTo(SkillSubState newState)
        {
            _currentState = newState;
            _stateStartTime = GetCurrentTime();
            _elapsedTime = 0f;
            _onStateChanged?.Invoke(newState);
        }

        private bool CanBeInterrupted(InterruptionSource source)
        {
            return source switch
            {
                InterruptionSource.DamageTaken => _skillData.CanBeInterruptedByDamage,
                InterruptionSource.MovementInput => _skillData.CanBeInterruptedByMovement,
                InterruptionSource.Stun => true,
                InterruptionSource.RollDodge => true,
                InterruptionSource.Parry => true,
                _ => false
            };
        }

        private float GetCurrentTime() => UnityEngine.Time.time;
        private bool Approximately(float a, float b) => UnityEngine.Mathf.Approximately(a, b);
    }
}

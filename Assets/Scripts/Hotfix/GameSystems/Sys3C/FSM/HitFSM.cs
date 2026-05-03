using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.Character;
using Core = Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// Hit 层状态
    /// </summary>
    public enum HitState
    {
        None = 0,
        Hit = 1,           // 普通受击（短暂僵直）
        Knockback = 2,     // 击退
        Launched = 3,      // 浮空
        Dizzy = 4,         // 眩晕
        Down = 5,          // 倒地
        GetUp = 6,         // 起身
        Death = 7          // 死亡
    }

    /// <summary>
    /// 受击状态数据
    /// </summary>
    public struct HitData
    {
        public float Damage;
        public Vector3 HitDirection;    // 受击方向
        public float KnockbackForce;    // 击退力度
        public float LaunchForce;       // 浮空力度
        public float StunDuration;       // 眩晕时长
        public bool IsCritical;         // 是否暴击

        public static HitData Default => new HitData
        {
            Damage = 0,
            HitDirection = Vector3.forward,
            KnockbackForce = 0,
            LaunchForce = 0,
            StunDuration = 0,
            IsCritical = false
        };
    }

    /// <summary>
    /// 受击状态机 - 管理受击/击退/倒地/死亡
    /// </summary>
    public class HitFSM
    {
        private readonly AnimationDriver _driver;
        private readonly FSMConfig _config;

        private HitState _currentState;
        private float _stateTimer;
        private HitData _hitData;
        private Vector3 _knockbackVelocity;

        public HitState CurrentState => _currentState;
        public float StateTimer => _stateTimer;
        public HitData CurrentHitData => _hitData;

        /// <summary>
        /// 是否处于霸体状态（死亡状态免疫所有伤害）
        /// </summary>
        public bool HasSuperArmor => _currentState == HitState.Death;

        /// <summary>
        /// 是否正在播放受击动画
        /// </summary>
        public bool IsPlayingHitAnimation => _currentState != HitState.None;

        /// <summary>
        /// 是否有受击优先级（Death 最高）
        /// </summary>
        public int Priority => _currentState switch
        {
            HitState.Death => 100,
            HitState.Down => 90,
            HitState.Launched => 80,
            HitState.Knockback => 70,
            HitState.Dizzy => 60,
            HitState.Hit => 50,
            HitState.GetUp => 40,
            HitState.None => 0,
            _ => 0
        };

        public event Action<HitState> OnStateChanged;
        public event Action OnHitComplete;
        public event Action OnDeathComplete;

        /// <summary>
        /// 动画结束时调用（由 StateMachineBehaviour 触发）
        /// </summary>
        public void OnAnimationEnd(string stateName)
        {
            Debug.Log($"[HitFSM] AnimationEnd: {stateName}");

            // 根据动画名称处理
            switch (stateName)
            {
                case "Hit":
                    if (_currentState == HitState.Hit)
                        Recover();
                    break;

                case "Knockback":
                    if (_currentState == HitState.Knockback)
                        Recover();
                    break;

                case "Launched":
                    // 浮空动画结束，可能进入倒地
                    if (_currentState == HitState.Launched)
                        TransitionTo(HitState.Down);
                    break;

                case "Dizzy":
                    if (_currentState == HitState.Dizzy)
                        Recover();
                    break;

                case "Down":
                    // 倒地动画结束，进入起身
                    if (_currentState == HitState.Down)
                        TransitionTo(HitState.GetUp);
                    break;

                case "GetUp":
                    if (_currentState == HitState.GetUp)
                        Recover();
                    break;

                case "Death":
                    // 死亡动画结束，触发死亡完成事件
                    OnDeathComplete?.Invoke();
                    break;
            }
        }

        public HitFSM(AnimationDriver driver)
            : this(driver, FSMConfig.Default)
        {
        }

        public HitFSM(AnimationDriver driver, FSMConfig config)
        {
            _driver = driver;
            _config = config;
            _currentState = HitState.None;
        }

        public void Update(float deltaTime)
        {
            if (_currentState == HitState.None) return;

            _stateTimer -= deltaTime;

            // 处理各状态的更新逻辑
            switch (_currentState)
            {
                case HitState.Knockback:
                    UpdateKnockback(deltaTime);
                    break;
                case HitState.Launched:
                    UpdateLaunched(deltaTime);
                    break;
                case HitState.Dizzy:
                    UpdateDizzy(deltaTime);
                    break;
                case HitState.Down:
                    UpdateDown(deltaTime);
                    break;
            }

            // 状态结束检查
            if (_stateTimer <= 0)
            {
                OnStateTimerEnd();
            }
        }

        /// <summary>
        /// 进入受击状态
        /// </summary>
        public bool EnterHit(HitData hitData)
        {
            // 死亡状态不可被打断
            if (_currentState == HitState.Death) return false;

            // 检查是否有更高优先级状态
            var newPriority = GetPriorityForHitData(hitData);
            if (newPriority <= Priority && _currentState != HitState.None)
            {
                // 优先级不够，不处理
                return false;
            }

            _hitData = hitData;

            // 根据伤害数据决定进入哪个状态
            HitState targetState;

            if (hitData.Damage >= 9999) // 致命伤害
            {
                targetState = HitState.Death;
            }
            else if (hitData.LaunchForce > 0)
            {
                targetState = HitState.Launched;
            }
            else if (hitData.KnockbackForce > 0)
            {
                targetState = HitState.Knockback;
            }
            else if (hitData.StunDuration > 0)
            {
                targetState = HitState.Dizzy;
            }
            else if (hitData.Damage >= 100) // 高伤害
            {
                targetState = HitState.Down;
            }
            else
            {
                targetState = HitState.Hit;
            }

            TransitionTo(targetState);
            return true;
        }

        /// <summary>
        /// 进入死亡状态
        /// </summary>
        public void EnterDeath()
        {
            _hitData = new HitData { Damage = 9999 };
            TransitionTo(HitState.Death);

            // 发送死亡事件
            Core.EventBus.Emit(new DeathEvent());
        }

        /// <summary>
        /// 进入倒地状态
        /// </summary>
        public void EnterDown()
        {
            TransitionTo(HitState.Down);
        }

        /// <summary>
        /// 受伤后恢复（受击动画结束）
        /// </summary>
        public void Recover()
        {
            if (_currentState != HitState.Death)
            {
                TransitionTo(HitState.None);
                OnHitComplete?.Invoke();
            }
        }

        /// <summary>
        /// 复活
        /// </summary>
        public void Resurrect()
        {
            if (_currentState == HitState.Death)
            {
                TransitionTo(HitState.None);
                OnDeathComplete?.Invoke();
            }
        }

        /// <summary>
        /// 重置到无受击状态
        /// </summary>
        public void Reset()
        {
            TransitionTo(HitState.None);
            _stateTimer = 0;
            _knockbackVelocity = Vector3.zero;
        }

        /// <summary>
        /// 获取当前受击位移（用于 CharacterController）
        /// </summary>
        public Vector3 GetKnockbackDisplacement()
        {
            if (_currentState == HitState.Knockback || _currentState == HitState.Launched)
            {
                return _knockbackVelocity * Time.deltaTime;
            }
            return Vector3.zero;
        }

        /// <summary>
        /// 获取击退方向
        /// </summary>
        public Vector3 GetHitDirection()
        {
            return _hitData.HitDirection;
        }

        private int GetPriorityForHitData(HitData data)
        {
            if (data.Damage >= 9999) return 100;
            if (data.LaunchForce > 0) return 80;
            if (data.KnockbackForce > 0) return 70;
            if (data.StunDuration > 0) return 60;
            if (data.Damage >= 100) return 50;
            return 40;
        }

        private void TransitionTo(HitState target)
        {
            if (_currentState == target) return;

            var previous = _currentState;
            _currentState = target;
            _stateTimer = GetStateDuration(target);

            // 驱动动画
            switch (target)
            {
                case HitState.Hit:
                    _driver.TriggerHit();
                    _driver.SetHitLayerWeight(1f);
                    break;

                case HitState.Knockback:
                    _driver.SetHitState(HitState.Knockback);
                    _driver.SetHitLayerWeight(1f);
                    // 计算击退速度
                    _knockbackVelocity = _hitData.HitDirection * _hitData.KnockbackForce;
                    break;

                case HitState.Launched:
                    _driver.SetHitState(HitState.Launched);
                    _driver.SetHitLayerWeight(1f);
                    // 浮空有垂直速度
                    _knockbackVelocity = _hitData.HitDirection * _hitData.KnockbackForce +
                                         Vector3.up * _hitData.LaunchForce;
                    break;

                case HitState.Dizzy:
                    _driver.SetHitState(HitState.Dizzy);
                    _driver.SetHitLayerWeight(1f);
                    break;

                case HitState.Down:
                    _driver.SetHitState(HitState.Down);
                    _driver.SetHitLayerWeight(1f);
                    break;

                case HitState.GetUp:
                    _driver.SetHitState(HitState.GetUp);
                    _driver.SetHitLayerWeight(1f);
                    break;

                case HitState.Death:
                    _driver.TriggerDeath();
                    _driver.SetHitLayerWeight(1f);
                    break;

                case HitState.None:
                    _driver.SetHitLayerWeight(0f);
                    _driver.SetHitState(HitState.None);
                    _knockbackVelocity = Vector3.zero;
                    break;
            }

            Debug.Log($"[HitFSM] {previous} -> {target} (duration: {_stateTimer}s)");
            OnStateChanged?.Invoke(target);
        }

        private float GetStateDuration(HitState state)
        {
            return state switch
            {
                HitState.Hit => _config.HitDuration,
                HitState.Knockback => _config.KnockbackDuration,
                HitState.Launched => _config.LaunchedDuration,
                HitState.Dizzy => _hitData.StunDuration > 0 ? _hitData.StunDuration : _config.DizzyDuration,
                HitState.Down => _config.DownDuration,
                HitState.GetUp => _config.GetUpDuration,
                HitState.Death => float.MaxValue, // 死亡状态持续
                _ => 0
            };
        }

        private void OnStateTimerEnd()
        {
            switch (_currentState)
            {
                case HitState.Hit:
                case HitState.Knockback:
                case HitState.Dizzy:
                    // 普通受击结束，恢复
                    Recover();
                    break;

                case HitState.Launched:
                    // 浮空结束后进入倒地
                    TransitionTo(HitState.Down);
                    _stateTimer = GetStateDuration(HitState.Down);
                    break;

                case HitState.Down:
                    // 倒地结束，起身
                    TransitionTo(HitState.GetUp);
                    _stateTimer = GetStateDuration(HitState.GetUp);
                    break;

                case HitState.GetUp:
                    // 起身结束，恢复正常
                    Recover();
                    break;
            }
        }

        private void UpdateKnockback(float deltaTime)
        {
            // 逐渐减速击退
            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, _config.KnockbackDeceleration * deltaTime);
        }

        private void UpdateLaunched(float deltaTime)
        {
            // 浮空时重力影响
            _knockbackVelocity.y -= _config.LaunchGravity * deltaTime;
            _knockbackVelocity.x *= _config.LaunchHorizontalDrag;
            _knockbackVelocity.z *= _config.LaunchHorizontalDrag;
        }

        private void UpdateDizzy(float deltaTime)
        {
            // 眩晕状态可以播放眩晕动画
            // 期间不接受输入
        }

        private void UpdateDown(float deltaTime)
        {
            // 倒地状态
            // 期间不接受输入
        }
    }
}
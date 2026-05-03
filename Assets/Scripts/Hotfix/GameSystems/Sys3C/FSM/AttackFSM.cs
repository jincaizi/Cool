using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// 攻击状态机 — 管理普攻连击和技能
    /// </summary>
    public class AttackFSM
    {
        private readonly AnimationDriver _driver;

        private AttackState _currentState;

        private int _comboCount;
        private int _framesInState;
        private bool _comboUnlocked;

        public event Action OnAttackCompleted;
        public event Action OnSkillCompleted;
        public event Action OnSkillOrAttackEnded;

        public AttackState CurrentState => _currentState;

        /// <summary>
        /// 技能是否可以播放（不打断当前攻击）
        /// </summary>
        public bool CanPlaySkill => _currentState == AttackState.Idle;

        /// <summary>
        /// 是否有霸体（某些技能有霸体帧）
        /// </summary>
        public bool HasSuperArmor => _currentState == AttackState.SkillR;

        /// <summary>
        /// 当前霸体剩余时间（秒）
        /// </summary>
        private float _superArmorTime = 0f;
        public float SuperArmorRemaining => _superArmorTime;

        public AttackFSM(AnimationDriver driver)
        {
            _driver = driver;
            _currentState = AttackState.Idle;
        }

        public void Update(float deltaTime)
        {
            // 更新霸体计时器
            if (_superArmorTime > 0)
            {
                _superArmorTime -= deltaTime;
                if (_superArmorTime < 0) _superArmorTime = 0;
            }

            if (_currentState == AttackState.Idle)
            {
                _comboCount = 0;
                _framesInState = 0;
                _comboUnlocked = false;
            }
            else
            {
                _framesInState++;

                if (!_comboUnlocked && _framesInState >= 5)
                {
                    _comboUnlocked = true;
                }
            }
        }

        public void RequestNormalAttack()
        {
            if (_currentState == AttackState.Idle)
            {
                _currentState = AttackState.Attack1;
                _comboCount = 1;
                _driver.SetAttackState(_currentState);
                _driver.TriggerAttack();
                Debug.Log("[AttackFSM] RequestAttack: Attack1");
            }
            else if (_currentState == AttackState.Attack1 && _comboUnlocked)
            {
                _currentState = AttackState.Attack2;
                _comboCount = 2;
                _driver.SetAttackState(_currentState);
                _driver.TriggerAttack();
                Debug.Log("[AttackFSM] RequestAttack: Attack2");
            }
        }

        public void RequestSkillQ()
        {
            if (_currentState == AttackState.Idle || _currentState == AttackState.Attack1 || _currentState == AttackState.Attack2)
            {
                _currentState = AttackState.SkillQ;
                _comboCount = 0;
                _framesInState = 0;
                _comboUnlocked = false;
                _driver.SetAttackState(_currentState);
                _driver.TriggerSkillQ();
                Debug.Log("[AttackFSM] RequestSkillQ");
            }
        }

        public void RequestSkillR(bool isGrounded)
        {
            if (!isGrounded) return;

            // 允许从普攻连击切换到技能R
            if (_currentState == AttackState.Idle || _currentState == AttackState.Attack1 || _currentState == AttackState.Attack2)
            {
                _currentState = AttackState.SkillR;
                _comboCount = 0;
                _framesInState = 0;
                _comboUnlocked = false;
                _driver.SetAttackState(_currentState);
                _driver.TriggerSkillR();

                // 技能R有霸体
                _superArmorTime = 0.5f;

                Debug.Log("[AttackFSM] RequestSkillR");
            }
        }

        /// <summary>
        /// 请求霸体（用于特定技能）
        /// </summary>
        public void RequestSuperArmor(float duration)
        {
            _superArmorTime = Mathf.Max(_superArmorTime, duration);
        }

        public void OnAnimationCompleted(string stateName)
        {
            switch (stateName)
            {
                case "Attack1":
                case "Attack2":
                    ReturnToIdle();
                    OnAttackCompleted?.Invoke();
                    OnSkillOrAttackEnded?.Invoke();
                    break;
                case "SkillQ":
                case "SkillR":
                    ReturnToIdle();
                    OnSkillCompleted?.Invoke();
                    OnSkillOrAttackEnded?.Invoke();
                    break;
            }
        }

        public void ReturnToIdle()
        {
            if (_currentState != AttackState.Idle)
            {
                _currentState = AttackState.Idle;
                _driver.SetAttackState(_currentState);
                Debug.Log("[AttackFSM] ReturnToIdle");
            }
        }

        public void ForceIdle()
        {
            ReturnToIdle();
        }

        private bool CanInterrupt()
        {
            return _currentState == AttackState.Idle;
        }
    }
}

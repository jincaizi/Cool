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

        public AttackState CurrentState => _currentState;

        public AttackFSM(AnimationDriver driver)
        {
            _driver = driver;
            _currentState = AttackState.Idle;
        }

        public void Update(float deltaTime)
        {
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
            if (_currentState == AttackState.Idle || CanInterrupt())
            {
                _currentState = AttackState.SkillQ;
                _driver.SetAttackState(_currentState);
                _driver.TriggerSkillQ();
                Debug.Log("[AttackFSM] RequestSkillQ");
            }
        }

        public void RequestSkillR(bool isGrounded)
        {
            if (!isGrounded) return;

            if (_currentState == AttackState.Idle || CanInterrupt())
            {
                _currentState = AttackState.SkillR;
                _driver.SetAttackState(_currentState);
                _driver.TriggerSkillR();
                Debug.Log("[AttackFSM] RequestSkillR");
            }
        }

        public void OnAnimationCompleted(string stateName)
        {
            switch (stateName)
            {
                case "Attack1":
                case "Attack2":
                    ReturnToIdle();
                    OnAttackCompleted?.Invoke();
                    break;
                case "SkillQ":
                case "SkillR":
                    ReturnToIdle();
                    OnSkillCompleted?.Invoke();
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

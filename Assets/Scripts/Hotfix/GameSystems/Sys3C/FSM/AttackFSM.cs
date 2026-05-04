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

        // 技能状态超时保护
        private float _skillStateTimer;
        private const float SKILL_TIMEOUT = 3f; // 技能最大持续时间

        // 技能R持续状态
        private float _skillRDuration;  // 当前持续时间
        private float _skillRMaxDuration = 3f;  // 最大持续时间（可配置）
        private bool _isSkillRActive;  // 是否正在持续技能中

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

            // 更新技能状态计时器
            if (_currentState == AttackState.SkillQ || _currentState == AttackState.SkillR_Start || _currentState == AttackState.SkillR_Loop)
            {
                _skillStateTimer += deltaTime;
                if (_skillStateTimer >= SKILL_TIMEOUT)
                {
                    Debug.LogWarning("[AttackFSM] Skill state timeout, forcing return to idle");
                    ReturnToIdle();
                }
            }

            // 更新技能R持续时间检测
            if (_isSkillRActive && _currentState == AttackState.SkillR_Loop)
            {
                _skillRDuration += deltaTime;
                if (_skillRMaxDuration > 0 && _skillRDuration >= _skillRMaxDuration)
                {
                    Debug.Log($"[AttackFSM] SkillR duration reached max ({_skillRMaxDuration}s), canceling");
                    CancelSkillR();
                }
            }

            if (_currentState == AttackState.Idle)
            {
                _comboCount = 0;
                _framesInState = 0;
                _comboUnlocked = false;
                _skillStateTimer = 0f;
                _skillRDuration = 0f;
                _isSkillRActive = false;
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
            Debug.Log($"[AttackFSM] RequestSkillQ called, current state: {_currentState}");
            if (_currentState == AttackState.Idle || _currentState == AttackState.Attack1 || _currentState == AttackState.Attack2)
            {
                _currentState = AttackState.SkillQ;
                _comboCount = 0;
                _framesInState = 0;
                _comboUnlocked = false;
                _driver.SetAttackState(_currentState);
                _driver.TriggerSkillQ();
                Debug.Log("[AttackFSM] RequestSkillQ: changed to SkillQ");
            }
            else
            {
                Debug.Log("[AttackFSM] RequestSkillQ blocked, current state is not Attack1/2");
            }
        }

        public void RequestSkillR(bool isGrounded)
        {
            Debug.Log($"[AttackFSM] RequestSkillR called, current state: {_currentState}, isGrounded: {isGrounded}");
            if (!isGrounded) return;

            if (_currentState == AttackState.Idle || _currentState == AttackState.Attack1 || _currentState == AttackState.Attack2)
            {
                _currentState = AttackState.SkillR_Start;
                _comboCount = 0;
                _framesInState = 0;
                _comboUnlocked = false;
                _isSkillRActive = true;
                _skillRDuration = 0f;
                _driver.SetAttackState(_currentState);
                _driver.TriggerSkillR();

                Debug.Log("[AttackFSM] RequestSkillR: changed to SkillR_Start");
            }
            else
            {
                Debug.Log("[AttackFSM] RequestSkillR blocked, current state is not Idle/Attack1/2");
            }
        }

        /// <summary>
        /// 请求霸体（用于特定技能）
        /// </summary>
        public void RequestSuperArmor(float duration)
        {
            _superArmorTime = Mathf.Max(_superArmorTime, duration);
        }

        /// <summary>
        /// 设置技能R的最大持续时间
        /// </summary>
        public void SetSkillRMaxDuration(float duration)
        {
            _skillRMaxDuration = duration;
        }

        /// <summary>
        /// 取消技能R（松开按键或超时）
        /// </summary>
        public void CancelSkillR()
        {
            if (_currentState == AttackState.SkillR_Start || _currentState == AttackState.SkillR_Loop)
            {
                Debug.Log("[AttackFSM] CancelSkillR called");
                _isSkillRActive = false;
                _skillRDuration = 0f;
                ReturnToIdle();
                OnSkillCompleted?.Invoke();
                OnSkillOrAttackEnded?.Invoke();
            }
        }

        /// <summary>
        /// SkillR起手动画完成，进入循环阶段
        /// </summary>
        public void OnSkillRStartCompleted()
        {
            if (_currentState == AttackState.SkillR_Start && _isSkillRActive)
            {
                Debug.Log("[AttackFSM] SkillR_Start completed, entering SkillR_Loop");
                _currentState = AttackState.SkillR_Loop;
                _driver.SetAttackState(_currentState);
                // SkillR_Loop动画会循环播放
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
                    OnSkillOrAttackEnded?.Invoke();
                    break;
                case "AttackQ":  // 动画状态在Animator中叫 AttackQ
                    ReturnToIdle();
                    OnSkillCompleted?.Invoke();
                    OnSkillOrAttackEnded?.Invoke();
                    break;
                case "SkillR_Start":
                    // 起手动画完成，进入循环阶段
                    OnSkillRStartCompleted();
                    break;
                case "SkillR_Loop":
                    // Loop状态下不应触发完成，除非被取消
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

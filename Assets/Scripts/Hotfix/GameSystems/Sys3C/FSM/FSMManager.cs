using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM.States;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// FSM 管理器 — 统一管理 BaseFSM 和 AttackFSM
    /// 监听 CharacterController 事件，驱动 Animator
    /// </summary>
    public class FSMManager
    {
        private readonly Hotfix.GameSystems.Sys3C.Character.CharacterController _characterController;
        private readonly Animator _animator;

        // Animator 参数哈希
        private static readonly int HASH_BaseState = Animator.StringToHash("BaseState");
        private static readonly int HASH_AttackState = Animator.StringToHash("AttackState");
        private static readonly int HASH_IsJumping = Animator.StringToHash("IsJumping");
        private static readonly int HASH_Attack = Animator.StringToHash("Attack");
        private static readonly int HASH_SkillQ = Animator.StringToHash("SkillQ");
        private static readonly int HASH_SkillR = Animator.StringToHash("SkillR");

        // 当前状态
        private BaseState _currentBaseState = BaseState.Idle;
        private AttackState _currentAttackState = AttackState.Idle;

        // 事件回调
        public event Action OnJumpEndCompleted;
        public event Action OnAttackCompleted;
        public event Action OnSkillCompleted;

        public FSMManager(Hotfix.GameSystems.Sys3C.Character.CharacterController characterController, Animator animator)
        {
            _characterController = characterController;
            _animator = animator;

            // 订阅 CharacterController 事件
            _characterController.OnJumpRequested += HandleJumpRequested;
            _characterController.OnLanded += HandleLanded;
            _characterController.OnDeath += HandleDeath;

            // 初始化 Animator 参数
            _animator.SetInteger(HASH_BaseState, (int)BaseState.Idle);
            _animator.SetInteger(HASH_AttackState, (int)AttackState.Idle);
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            SyncFromCharacterData();
        }

        /// <summary>
        /// 从 CharacterData 同步状态
        /// </summary>
        private void SyncFromCharacterData()
        {
            var data = _characterController.Data;

            // 同步 BaseState
            if (data.BaseState != _currentBaseState)
            {
                _currentBaseState = data.BaseState;
                _animator.SetInteger(HASH_BaseState, (int)_currentBaseState);

                // 更新 IsJumping
                bool isJumping = _currentBaseState == BaseState.JumpStart ||
                                 _currentBaseState == BaseState.JumpAir ||
                                 _currentBaseState == BaseState.JumpEnd;
                _animator.SetBool(HASH_IsJumping, isJumping);
            }
        }

        /// <summary>
        /// 请求普通攻击
        /// </summary>
        public void RequestNormalAttack()
        {
            if (_currentAttackState == AttackState.Idle)
            {
                _currentAttackState = AttackState.Attack1;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
                _animator.SetTrigger(HASH_Attack);
            }
            else if (_currentAttackState == AttackState.Attack1)
            {
                // 连击到 Attack2
                _currentAttackState = AttackState.Attack2;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
                _animator.SetTrigger(HASH_Attack);
            }
            // Attack2 后不能再连击，返回 AttackIdle
        }

        /// <summary>
        /// 请求技能Q
        /// </summary>
        public void RequestSkillQ()
        {
            if (_currentAttackState == AttackState.Idle || CanInterrupt())
            {
                _currentAttackState = AttackState.SkillQ;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
                _animator.SetTrigger(HASH_SkillQ);
            }
        }

        /// <summary>
        /// 请求技能R
        /// </summary>
        public void RequestSkillR()
        {
            // SkillR 不可在跳跃中使用
            if (_characterController.Data.BaseState == BaseState.JumpStart ||
                _characterController.Data.BaseState == BaseState.JumpAir)
            {
                return;
            }

            if (_currentAttackState == AttackState.Idle || CanInterrupt())
            {
                _currentAttackState = AttackState.SkillR;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
                _animator.SetTrigger(HASH_SkillR);
            }
        }

        /// <summary>
        /// 动画完成回调（由 StateMachineBehaviour 调用）
        /// </summary>
        public void OnAnimationCompleted(string stateName)
        {
            switch (stateName)
            {
                case "JumpEnd":
                    _characterController.FinishJump();
                    OnJumpEndCompleted?.Invoke();
                    break;
                case "Attack1":
                case "Attack2":
                    ReturnToAttackIdle();
                    OnAttackCompleted?.Invoke();
                    break;
                case "SkillQ":
                case "SkillR":
                    ReturnToAttackIdle();
                    OnSkillCompleted?.Invoke();
                    break;
            }
        }

        private void ReturnToAttackIdle()
        {
            if (_currentAttackState != AttackState.Idle)
            {
                _currentAttackState = AttackState.Idle;
                _animator.SetInteger(HASH_AttackState, (int)_currentAttackState);
            }
        }

        private bool CanInterrupt()
        {
            // 某些状态可被打断
            return _currentAttackState == AttackState.Idle;
        }

        private void HandleJumpRequested()
        {
            // Jump 由 CharacterController 处理
        }

        private void HandleLanded()
        {
            // 落地由 CharacterController 检测
        }

        private void HandleDeath()
        {
            _currentBaseState = BaseState.Death;
            _animator.SetInteger(HASH_BaseState, (int)_currentBaseState);
        }
    }
}

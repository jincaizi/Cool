using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Animation.StateBehaviours;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// FSM 协调者 — 管理 BaseFSM 和 AttackFSM
    /// 只负责协调，不处理具体状态逻辑
    /// </summary>
    public class FSMManager
    {
        private readonly CharacterController _characterController;
        private readonly AnimationDriver _driver;

        private readonly BaseFSM _baseFSM;
        private readonly AttackFSM _attackFSM;
        private readonly StateTransitionTable _transitionTable;

        public event Action OnJumpEndCompleted;
        public event Action OnAttackCompleted;
        public event Action OnSkillCompleted;

        public FSMManager(CharacterController characterController, Animator animator)
        {
            _characterController = characterController;
            _driver = new AnimationDriver(animator);

            _transitionTable = new StateTransitionTable();
            _baseFSM = new BaseFSM(_driver, _transitionTable);
            _attackFSM = new AttackFSM(_driver);

            _characterController.OnJumpRequested += HandleJumpRequested;
            _characterController.OnLanded += HandleLanded;
            _characterController.OnDeath += HandleDeath;

            _baseFSM.OnStateChanged += HandleBaseStateChanged;
            _attackFSM.OnAttackCompleted += () => OnAttackCompleted?.Invoke();
            _attackFSM.OnSkillCompleted += () => OnSkillCompleted?.Invoke();

            BaseStateBehaviour.SetCallback(_driver, HandleAnimationCompleted);
            AttackStateBehaviour.SetCallback(_driver, HandleAnimationCompleted);
            HitStateBehaviour.SetCallback(_driver, HandleHitCompleted);

            Debug.Log("[FSMManager] Initialized");
        }

        public void Update(float deltaTime)
        {
            var data = _characterController.Data;

            _baseFSM.Update(data, _attackFSM.CurrentState);
            _attackFSM.Update(deltaTime);
        }

        public void RequestNormalAttack()
        {
            _attackFSM.RequestNormalAttack();
        }

        public void RequestSkillQ()
        {
            _attackFSM.RequestSkillQ();
        }

        public void RequestSkillR()
        {
            _attackFSM.RequestSkillR(_characterController.IsGrounded);
        }

        public void TriggerHit()
        {
            _attackFSM.ForceIdle();
            _driver.TriggerHit();
            _driver.SetIsHit(true);
            _driver.SetHitLayerWeight(1f);
        }

        private void HandleAnimationCompleted(string stateName)
        {
            Debug.Log($"[FSMManager] AnimationCompleted: {stateName}");

            switch (stateName)
            {
                case "JumpEnd":
                    _characterController.FinishJump();
                    OnJumpEndCompleted?.Invoke();
                    break;
                case "Attack1":
                case "Attack2":
                case "SkillQ":
                case "SkillR":
                    _attackFSM.OnAnimationCompleted(stateName);
                    break;
            }
        }

        private void HandleHitCompleted()
        {
            _driver.SetIsHit(false);
            _driver.SetHitLayerWeight(0f);
        }

        private void HandleJumpRequested()
        {
            // Jump 由 CharacterController 处理
        }

        private void HandleLanded()
        {
            // 落地由 BaseFSM 检测
        }

        private void HandleDeath()
        {
            Debug.Log("[FSMManager] HandleDeath");
            _baseFSM.LockState(BaseState.Death);
            _attackFSM.ForceIdle();
        }

        private void HandleBaseStateChanged(BaseState state)
        {
            // 可扩展：通知其他系统
        }
    }
}

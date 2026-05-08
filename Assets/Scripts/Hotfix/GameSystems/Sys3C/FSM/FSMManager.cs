using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Animation.StateBehaviours;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    public class FSMManager
    {
        private readonly Hotfix.GameSystems.Sys3C.Character.CharacterController _characterController;
        private readonly Animator _animator;

        private readonly BaseFSM _baseFSM;
        private readonly HitFSM _hitFSM;
        private readonly StateCoordinator _stateCoordinator;

        public event Action OnJumpEndCompleted;
        public event Action OnAttackAnimationCompleted;
        public event Action OnHitCompleted;
        public event Action OnDeath;

        public StateCoordinator Coordinator => _stateCoordinator;
        public HitFSM HitFSM => _hitFSM;

        public FSMManager(
            Hotfix.GameSystems.Sys3C.Character.CharacterController characterController,
            Animator animator)
        {
            _characterController = characterController;
            _animator = animator;

            // Initialize layer weights (was in deleted AnimationDriver constructor)
            _animator.SetLayerWeight(AnimHashes.AttackLayerIndex, 0f);
            _animator.SetLayerWeight(AnimHashes.HitLayerIndex, 0f);

            var transitionTable = new StateTransitionTable();
            _baseFSM = new BaseFSM(_animator, transitionTable);
            _hitFSM = new HitFSM(_animator);

            _stateCoordinator = new StateCoordinator(_baseFSM, _hitFSM);
            _stateCoordinator.Initialize();

            _characterController.SetStateCoordinator(_stateCoordinator);

            _characterController.OnJumpRequested += HandleJumpRequested;
            _characterController.OnLanded += HandleLanded;
            _characterController.OnLeftGround += HandleLeftGround;
            _characterController.OnDeath += HandleDeath;

            _baseFSM.OnStateChanged += HandleBaseStateChanged;
            _hitFSM.OnHitComplete += HandleHitComplete;
            _hitFSM.OnDeathComplete += HandleDeathComplete;

            BaseStateBehaviour.SetCallback(HandleAnimationCompleted);
            AttackStateBehaviour.SetCallback(HandleAnimationCompleted);
            HitStateBehaviour.SetCallback(HandleHitAnimationCompleted);
        }

        public void Update(float deltaTime)
        {
            var data = _characterController.Data;

            bool isAttacking = _stateCoordinator.ActiveLayer == LayerType.Attack;

            _baseFSM.Update(data, isAttacking);
            _stateCoordinator.Update(deltaTime);

            UpdateBlendParameter(data);
        }

        private void UpdateBlendParameter(CharacterData data)
        {
            if (data.BaseState == BaseState.Idle ||
                data.BaseState == BaseState.Move ||
                data.BaseState == BaseState.Sprint ||
                data.BaseState == BaseState.Locomotion)
            {
                _animator.SetFloat(AnimHashes.Blend, data.MovementSpeed);
            }
        }

        public bool TryJump()
        {
            return _stateCoordinator.TryRequestJump();
        }

        public void HandleDamage(int sourceId, float damage, Vector3 hitDirection,
            float knockbackForce = 0, float launchForce = 0, float stunDuration = 0, bool isCritical = false)
        {
            var damageEvent = new Core.Events.DamageEvent(sourceId, 0, damage, isCritical)
            {
                HitDirection = hitDirection,
                KnockbackForce = knockbackForce,
                LaunchForce = launchForce,
                StunDuration = stunDuration
            };

            _stateCoordinator.HandleDamage(damageEvent);
        }

        public void RequestDeath()
        {
            _stateCoordinator.HandleDeath();
        }

        public void RequestResurrect()
        {
            _stateCoordinator.HandleResurrect();
        }

        public void TriggerHit(float knockbackForce = 0f)
        {
            var hitData = new HitData
            {
                Damage = 10,
                KnockbackForce = knockbackForce,
                HitDirection = Vector3.back
            };
            _hitFSM.EnterHit(hitData);
            _stateCoordinator.SetActiveLayer(LayerType.Hit);
        }

        private void HandleAnimationCompleted(string stateName)
        {
            switch (stateName)
            {
                case "JumpEnd":
                    _characterController.FinishJump();
                    OnJumpEndCompleted?.Invoke();
                    break;
                case "Attack1":
                case "Attack2":
                    _animator.ResetTrigger(AnimHashes.Attack);
                    _characterController.LockMovement = false;
                    _characterController.LockRotation = false;
                    OnAttackAnimationCompleted?.Invoke();
                    break;
                case "AttackSkill":
                    // Generic attack/skill animation completed (SkillQ, SkillR, etc.)
                    OnAttackAnimationCompleted?.Invoke();
                    break;
            }
        }

        private void HandleHitAnimationCompleted(string stateName)
        {
            _hitFSM.OnAnimationEnd(stateName);
        }

        private void HandleHitComplete()
        {
            _animator.SetBool(AnimHashes.IsHit, false);
            _animator.SetLayerWeight(AnimHashes.HitLayerIndex, 0f);
            OnHitCompleted?.Invoke();
        }

        private void HandleDeathComplete()
        {
            OnDeath?.Invoke();
        }

        private void HandleJumpRequested() { }
        private void HandleLanded() { }
        private void HandleLeftGround() { }
        private void HandleDeath()
        {
            _stateCoordinator.HandleDeath();
        }

        private void HandleBaseStateChanged(BaseState state) { }
    }
}

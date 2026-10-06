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
        public bool CanDefend => _stateCoordinator.CanDefend;

        public FSMManager(
            Hotfix.GameSystems.Sys3C.Character.CharacterController characterController,
            Animator animator)
        {
            _characterController = characterController;
            _animator = animator;

            // Initialize layer weights (was in deleted AnimationDriver constructor)
            _animator.SetLayerWeight(AnimHashes.AttackLayerIndex, 0f);
            _animator.SetLayerWeight(AnimHashes.HitLayerIndex, 0f);
            _animator.SetLayerWeight(AnimHashes.DefendLayerIndex, 0f);

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
            // Blend 只影响 Locomotion Blend Tree，跳跃/受击状态使用独立动画剪辑，写入无副作用。
            // 不能用 data.BaseState 门控：落地后 BaseState 可能滞留在 JumpEnd
            // （JumpEnd 完成回调依赖 Animator 播放完整剪辑，连跳时不可靠），
            // 一旦被门控跳过，Blend 会永远停在跳跃时清零的 0，腿部失去移动动画（漂移）。
            _animator.SetFloat(AnimHashes.Blend, data.MovementSpeed);
            // 举盾时移动减速（0.4×），Locomotion 状态速度参数同步放慢走步循环，避免脚步滑步
            _animator.SetFloat(AnimHashes.LocomotionSpeed,
                _characterController.IsDefending
                    ? Hotfix.GameSystems.Sys3C.Character.CharacterController.DefendSpeedMultiplier
                    : 1f);
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

            // 既有：免疫判定 / 扣韧性 / 发事件
            _stateCoordinator.HandleDamage(damageEvent);

            // P6 ①：受击表现接线。DamageEvent 的字段与 HitData 同构（Damage/KnockbackForce/
            // LaunchForce/StunDuration/HitDirection），直接映射，无需新增事件或协议。
            // 与既有 HandleShieldBreak(:132)/TriggerHit(:144) 同款模式：EnterHit + 切 Hit 层。
            var hitData = new HitData
            {
                Damage = damage,
                HitDirection = hitDirection,
                KnockbackForce = knockbackForce,
                LaunchForce = launchForce,
                StunDuration = stunDuration
            };

            // 返回值 false = 被更高优先级状态拒绝（已在更高优先级状态 / 已死亡）——尊重既有优先级，不切层
            if (_hitFSM.EnterHit(hitData))
                _stateCoordinator.SetActiveLayer(LayerType.Hit);
        }

        public void RequestDeath()
        {
            _stateCoordinator.HandleDeath();
        }

        public void RequestResurrect()
        {
            _stateCoordinator.HandleResurrect();
        }

        public void EnterDefend()
        {
            // 举盾姿势放上半身蒙版层，Base层保持 Locomotion ——
            // 否则锁 BaseState=Defend 会让 Animator 播静态全身剪辑，移动时腿部无动画
            _animator.SetBool(AnimHashes.IsDefending, true);
            _animator.SetLayerWeight(AnimHashes.DefendLayerIndex, 1f);
        }

        public void ExitDefend()
        {
            _animator.SetBool(AnimHashes.IsDefending, false);
            _animator.SetLayerWeight(AnimHashes.DefendLayerIndex, 0f);
        }

        public void HandleShieldBreak(HitData hitData)
        {
            ExitDefend();
            _hitFSM.EnterHit(new HitData
            {
                Damage = hitData.Damage,
                HitDirection = hitData.HitDirection,
                StunDuration = 1.5f
            });
            _stateCoordinator.SetActiveLayer(LayerType.Hit);
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
                case "AttackSkill":
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
            // P6 ①：把活动层交还 Base——与攻击流 Sys3CEntry.cs:390 的既有做法一致。
            // 不归还则 ActiveLayer 滞留在 Hit，FSMManager.Update:67 的 isAttacking 读数失真。
            _stateCoordinator.UnlockAndReturnToBase();
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

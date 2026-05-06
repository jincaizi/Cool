using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Animation.StateBehaviours;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// FSM 协调者 — 管理 BaseFSM、AttackFSM 和 HitFSM
    /// 通过 StateCoordinator 进行层间协调
    /// </summary>
    public class FSMManager
    {
        private readonly Hotfix.GameSystems.Sys3C.Character.CharacterController _characterController;
        private readonly AnimationDriver _driver;

        private readonly BaseFSM _baseFSM;
        private readonly AttackFSM _attackFSM;
        private readonly HitFSM _hitFSM;
        private readonly StateCoordinator _stateCoordinator;
        private readonly Skill.SkillDashComponent _dashComponent;

        public event Action OnJumpEndCompleted;
        public event Action OnAttackCompleted;
        public event Action OnAttackActivated;
        public event Action OnSkillCompleted;
        public event Action OnHitCompleted;
        public event Action OnDeath;

        /// <summary>
        /// 获取 StateCoordinator（供其他系统使用）
        /// </summary>
        public StateCoordinator Coordinator => _stateCoordinator;

        /// <summary>
        /// 获取 HitFSM
        /// </summary>
        public HitFSM HitFSM => _hitFSM;

        public FSMManager(Hotfix.GameSystems.Sys3C.Character.CharacterController characterController, Animator animator, AnimationDriver driver)
        {
            _characterController = characterController;
            _driver = driver;

            var transitionTable = new StateTransitionTable();
            _baseFSM = new BaseFSM(_driver, transitionTable);
            _attackFSM = new AttackFSM(_driver);
            _hitFSM = new HitFSM(_driver);

            // 创建 StateCoordinator 并初始化
            _stateCoordinator = new StateCoordinator(_baseFSM, _attackFSM, _hitFSM);
            _stateCoordinator.Initialize();

            // 初始化 SkillDashComponent
            var unityController = characterController.GetType()
                .GetField("_controller", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(characterController) as UnityEngine.CharacterController;
            _dashComponent = new Skill.SkillDashComponent(unityController, characterController.Transform);

            // 传递 dashComponent 给 AttackFSM
            _attackFSM.SetDashComponent(_dashComponent);

            // 设置 CharacterController 的 StateCoordinator 引用
            _characterController.SetStateCoordinator(_stateCoordinator);

            // 订阅 CharacterController 事件
            _characterController.OnJumpRequested += HandleJumpRequested;
            _characterController.OnLanded += HandleLanded;
            _characterController.OnLeftGround += HandleLeftGround;
            _characterController.OnDeath += HandleDeath;

            // 订阅 FSM 事件
            _baseFSM.OnStateChanged += HandleBaseStateChanged;
            _attackFSM.OnAttackCompleted += () => OnAttackCompleted?.Invoke();
            _attackFSM.OnSkillCompleted += () => OnSkillCompleted?.Invoke();
            _attackFSM.OnSkillOrAttackEnded += UnlockRotation;

            // 订阅 HitFSM 事件
            _hitFSM.OnHitComplete += HandleHitComplete;
            _hitFSM.OnDeathComplete += HandleDeathComplete;

            // 设置 Animation Callbacks
            BaseStateBehaviour.SetCallback(_driver, HandleAnimationCompleted);
            AttackStateBehaviour.SetCallback(_driver, HandleAnimationCompleted);
            HitStateBehaviour.SetCallback(_driver, HandleHitAnimationCompleted);

            Debug.Log("[FSMManager] Initialized with StateCoordinator");
        }

        public void Update(float deltaTime)
        {
            var data = _characterController.Data;

            _baseFSM.Update(data, _attackFSM.CurrentState);
            _attackFSM.Update(deltaTime);
            _stateCoordinator.Update(deltaTime);

            // 更新 Blend 参数（驱动 Locomotion Blend Tree）
            UpdateBlendParameter(data);

            // 更新 SkillQ 突进
            if (_attackFSM.CurrentState == AttackState.SkillQ && _dashComponent.IsDashing)
            {
                _dashComponent.Update();
            }
        }

        /// <summary>
        /// 更新 Blend 参数，用于 Blend Tree 动画混合
        /// </summary>
        private void UpdateBlendParameter(CharacterData data)
        {
            // 当处于 Locomotion 相关状态时，根据移动速度更新 Blend
            // 包括：Idle(0), Move(1), Sprint(2), Locomotion(7)
            if (data.BaseState == BaseState.Idle ||
                data.BaseState == BaseState.Move ||
                data.BaseState == BaseState.Sprint ||
                data.BaseState == BaseState.Locomotion)
            {
                _driver.SetBlend(data.MovementSpeed);
            }
            // 跳跃/死亡时保持 Blend=0（由 BaseFSM.TransitionTo 设置）
        }

        /// <summary>
        /// 请求普通攻击
        /// </summary>
        public bool TryAttack()
        {
            return _stateCoordinator.TryRequestAttack();
        }

        /// <summary>
        /// 请求技能
        /// </summary>
        public bool TrySkill(int skillId, string skillName)
        {
            return _stateCoordinator.TryRequestSkill(skillId, skillName);
        }

        /// <summary>
        /// 请求跳跃
        /// </summary>
        public bool TryJump()
        {
            return _stateCoordinator.TryRequestJump();
        }

        /// <summary>
        /// 处理伤害（供外部系统调用）
        /// </summary>
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

        /// <summary>
        /// 请求死亡
        /// </summary>
        public void RequestDeath()
        {
            _stateCoordinator.HandleDeath();
        }

        /// <summary>
        /// 请求复活
        /// </summary>
        public void RequestResurrect()
        {
            _stateCoordinator.HandleResurrect();
        }

        public void RequestNormalAttack()
        {
            _attackFSM.RequestNormalAttack();
            OnAttackActivated?.Invoke();
        }

        public void RequestSkillQ()
        {
            _characterController.LockRotation = true;
            _characterController.LockMovement = true;  // 锁定移动，防止普通移动干扰突进
            _attackFSM.RequestSkillQ();

            // 启动突进，方向为角色正前方
            Vector3 forward = _characterController.Transform.forward;
            _attackFSM.StartSkillQDash(forward);
        }

        public void RequestSkillR()
        {
            _characterController.LockRotation = true;
            _attackFSM.RequestSkillR(_characterController.IsGrounded);
        }

        /// <summary>
        /// 设置技能R的最大持续时间
        /// </summary>
        public void SetSkillRMaxDuration(float duration)
        {
            _attackFSM.SetSkillRMaxDuration(duration);
        }

        /// <summary>
        /// 取消技能R（由Sys3CEntry调用）
        /// </summary>
        public void CancelSkillR()
        {
            // 安全冗余：直接解锁旋转和移动（即使 AttackFSM.CancelSkillR 不触发事件）
            _characterController.LockRotation = false;
            _characterController.LockMovement = false;

            _attackFSM.CancelSkillR();
        }

        /// <summary>
        /// 解锁角色旋转（技能结束后调用）
        /// </summary>
        public void UnlockRotation()
        {
            _characterController.LockRotation = false;
        }

        /// <summary>
        /// 获取 AttackFSM 的技能可用性
        /// </summary>
        public bool CanPlaySkill => _attackFSM.CanPlaySkill;

        /// <summary>
        /// 当前是否处于 SkillR 状态（Start 或 Loop）
        /// </summary>
        public bool IsInSkillRState =>
            _attackFSM.CurrentState == AttackState.SkillR_Start ||
            _attackFSM.CurrentState == AttackState.SkillR_Loop;

        /// <summary>
        /// 通知技能层需要朝向相机方向（由 CharacterController 调用）
        /// </summary>
        public void UpdateSkillRotation(Vector3 cameraForward)
        {
            // 当处于攻击/技能状态时，不允许角色旋转
            // 角色应该在技能动画播放期间保持朝向
        }

        /// <summary>
        /// 手动触发受击（用于测试或 AI）
        /// </summary>
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

        public void DebugLogStates()
        {
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
                case "AttackQ":  // SkillQ 动画状态在Animator中叫 AttackQ
                    // 重要：先调用 FSM 的 OnAnimationCompleted 将状态重置为 Idle
                    // 这确保即使 OnStateExit 中的条件检查失败，FSM 状态也能正确恢复
                    _attackFSM.OnAnimationCompleted(stateName);

                    // 重置所有 triggers（防止下一帧动画循环播放）
                    _driver.ResetAttackTrigger();
                    _driver.ResetSkillQTrigger();
                    _driver.ResetSkillRTrigger();

                    // 恢复移动锁定
                    _characterController.LockMovement = false;
                    _characterController.LockRotation = false;
                    break;
                case "SkillR_Start":
                    _attackFSM.OnAnimationCompleted(stateName);
                    // 不重置SkillR trigger，保持状态
                    // 注意：LockRotation 在 R 键释放时由 CancelSkillR -> OnSkillOrAttackEnded -> UnlockRotation 解锁
                    break;
            }
        }

        private void HandleHitAnimationCompleted(string stateName)
        {
            _hitFSM.OnAnimationEnd(stateName);
        }

        private void HandleHitComplete()
        {
            _driver.SetIsHit(false);
            _driver.SetHitLayerWeight(0f);
            OnHitCompleted?.Invoke();
        }

        private void HandleDeathComplete()
        {
            OnDeath?.Invoke();
        }

        private void HandleJumpRequested()
        {
            // Jump 由 CharacterController 处理
        }

        private void HandleLanded()
        {
            // 落地由 BaseFSM 检测
        }

        private void HandleLeftGround()
        {
            // 可以在这里通知其他系统（如 AI、任务系统等）
        }

        private void HandleDeath()
        {
            _stateCoordinator.HandleDeath();
        }

        private void HandleBaseStateChanged(BaseState state)
        {
            // 可扩展：通知其他系统
        }
    }
}
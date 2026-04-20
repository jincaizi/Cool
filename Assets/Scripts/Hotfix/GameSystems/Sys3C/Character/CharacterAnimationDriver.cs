using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色动画驱动器 — 通过 Animator 参数驱动角色动画状态机
    /// 负责：跳跃三段、攻击连招、战斗状态切换、移动/奔跑状态
    /// </summary>
    public class CharacterAnimationDriver
    {
        private readonly Animator _animator;

        // === Cached parameter hashes ===
        private static readonly int HASH_State = Animator.StringToHash("State");
        private static readonly int HASH_SubState = Animator.StringToHash("SubState");
        private static readonly int HASH_IsBattle = Animator.StringToHash("IsBattle");
        private static readonly int HASH_IsMoving = Animator.StringToHash("IsMoving");
        private static readonly int HASH_IsJumping = Animator.StringToHash("IsJumping");
        private static readonly int HASH_IsDead = Animator.StringToHash("IsDead");
        private static readonly int HASH_JumpPhase = Animator.StringToHash("JumpPhase");
        private static readonly int HASH_AttackPhase = Animator.StringToHash("AttackPhase");
        private static readonly int HASH_Speed = Animator.StringToHash("Speed");

        // === State hashes ===
        private static readonly int HASH_JumpStart = Animator.StringToHash("JumpStart");
        private static readonly int HASH_JumpEnd = Animator.StringToHash("JumpEnd");
        private static readonly int HASH_Idle = Animator.StringToHash("Idle");
        private static readonly int HASH_BattleIdle = Animator.StringToHash("BattleIdle");

        // === State → Callback mapping ===
        private readonly Dictionary<int, Action> _onStateEnterCallbacks = new Dictionary<int, Action>();
        private readonly Dictionary<int, Action> _onStateExitCallbacks = new Dictionary<int, Action>();

        // === Phase tracking ===
        private bool _isInCombat;
        private int _currentComboCount;
        private const int MAX_COMBO = 4;

        public CharacterAnimationDriver(Animator animator)
        {
            _animator = animator ?? throw new ArgumentNullException(nameof(animator));

            // 注册状态回调
            RegisterCallbacks();
        }

        private void RegisterCallbacks()
        {
            // JumpStart 进入 → 驱动到 JumpAir
            _onStateEnterCallbacks[HASH_JumpStart] = () =>
            {
                _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.Start);
                _animator.SetInteger(HASH_State, (int)CharacterState.JumpStart);
            };

    // JumpEnd 进入 → 设置落地动画
            _onStateEnterCallbacks[HASH_JumpEnd] = () =>
            {
                _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.End);
                _animator.SetBool(HASH_IsJumping, false);
            };

            // 攻击状态进入 → 记录连击阶段
            foreach (var attackInfo in GetAttackStateInfos())
            {
                int hash = attackInfo.Hash;
                int index = attackInfo.Index;
                Action enterCallback = () =>
                {
                    _currentComboCount = index;
                    _animator.SetInteger(HASH_SubState, index);
                    _animator.SetInteger(HASH_AttackPhase, index);
                    if (!_isInCombat)
                        EnterBattle();
                };
                _onStateEnterCallbacks[hash] = enterCallback;
            }
        }

        private static IEnumerable<(int Hash, int Index)> GetAttackStateInfos()
        {
            yield return (Animator.StringToHash("Attack1"), 1);
            yield return (Animator.StringToHash("Attack2"), 2);
            yield return (Animator.StringToHash("Attack3"), 3);
            yield return (Animator.StringToHash("Attack4"), 4);
        }

        /// <summary>
        /// 每帧更新 — 驱动基础 Animator 参数
        /// </summary>
        public void Update(CharacterData data)
        {
            if (_animator == null) return;

            _animator.SetFloat(HASH_Speed, data.Velocity.magnitude, 0.1f, Time.deltaTime);
            _animator.SetBool(HASH_IsBattle, data.IsBattle || _isInCombat);
            _animator.SetBool(HASH_IsMoving, data.State == CharacterState.Move || data.State == CharacterState.Run);
            _animator.SetBool(HASH_IsDead, data.State == CharacterState.Death);
        }

        /// <summary>
        /// 由 CharacterStateBehaviour 在状态进入时调用
        /// </summary>
        public void OnStateEntered(int shortNameHash)
        {
            if (_onStateEnterCallbacks.TryGetValue(shortNameHash, out var callback))
                callback();
        }

        /// <summary>
        /// 由 CharacterStateBehaviour 在状态退出时调用
        /// </summary>
        public void OnStateExited(int shortNameHash)
        {
            if (_onStateExitCallbacks.TryGetValue(shortNameHash, out var callback))
                callback();
        }

        // ============================================
        // Public API — 供外部（CharacterController、Input 等）调用
        // ============================================

        /// <summary>
        /// 进入战斗状态（攻击时自动调用）
        /// </summary>
        public void EnterBattle()
        {
            _isInCombat = true;
            _animator.SetBool(HASH_IsBattle, true);
        }

        /// <summary>
        /// 退出战斗状态（预留，后续扩展）
        /// </summary>
        public void ExitBattle()
        {
            _isInCombat = false;
            _animator.SetBool(HASH_IsBattle, false);
        }

        /// <summary>
        /// 设置移动状态
        /// </summary>
        public void SetMoving(bool moving)
        {
            _animator.SetBool(HASH_IsMoving, moving);
        }

/// <summary>
        /// 开始跳跃 — 驱动 JumpStart 动画
        /// 注意：物理状态由 CharacterController.RequestJump() 处理
        /// </summary>
        public void OnJumpStart()
        {
            _animator.SetBool(HASH_IsJumping, true);
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.Start);
        }

/// <summary>
        /// 落地检测时调用（CharacterController 地面检测触发）
        /// 注意：物理状态由 CharacterController 处理，这里只驱动动画
        /// </summary>
        public void OnLanding()
        {
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.End);
            _animator.SetBool(HASH_IsJumping, false);
        }

        /// <summary>
        /// 开始攻击（支持地面和空中）
        /// </summary>
        public void OnAttack(int attackIndex)
        {
            if (attackIndex < 1 || attackIndex > MAX_COMBO) return;
            if (!_isInCombat)
                EnterBattle();

            _currentComboCount = attackIndex;
            _animator.SetInteger(HASH_SubState, attackIndex);
            _animator.SetInteger(HASH_AttackPhase, attackIndex);
        }

        /// <summary>
        /// 空中攻击（叠加动画）
        /// </summary>
        public void OnAttackInAir(int attackIndex)
        {
            OnAttack(attackIndex);
        }

        /// <summary>
        /// 攻击完成（CharacterStateBehaviour 自动调用）
        /// </summary>
        public void OnAttackComplete()
        {
            _animator.SetInteger(HASH_SubState, 0);
            _animator.SetInteger(HASH_AttackPhase, 0);
        }

        /// <summary>
        /// 尝试连击下一击（在 ComboWindow 期间调用）
        /// </summary>
        public void TryComboNext()
        {
            if (_currentComboCount < MAX_COMBO)
                OnAttack(_currentComboCount + 1);
        }

        /// <summary>
        /// 死亡 — 停止所有状态，播放死亡动画
        /// </summary>
        public void OnDeath()
        {
            _animator.SetBool(HASH_IsDead, true);
            _animator.SetBool(HASH_IsJumping, false);
            _animator.SetBool(HASH_IsMoving, false);
            _animator.SetInteger(HASH_State, (int)CharacterState.Death);
        }
    }
}

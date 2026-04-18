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

        // Cached parameter hashes
        private static readonly int HASH_State = Animator.StringToHash("State");
        private static readonly int HASH_SubState = Animator.StringToHash("SubState");
        private static readonly int HASH_IsBattle = Animator.StringToHash("IsBattle");
        private static readonly int HASH_IsMoving = Animator.StringToHash("IsMoving");
        private static readonly int HASH_IsJumping = Animator.StringToHash("IsJumping");
        private static readonly int HASH_IsDead = Animator.StringToHash("IsDead");
        private static readonly int HASH_JumpPhase = Animator.StringToHash("JumpPhase");
        private static readonly int HASH_AttackPhase = Animator.StringToHash("AttackPhase");
        private static readonly int HASH_Speed = Animator.StringToHash("Speed");

        // Current phase tracking
        private JumpPhase _currentJumpPhase = JumpPhase.None;
        private AttackPhase _currentAttackPhase = AttackPhase.None;
        private bool _isInCombat = false;
        private int _comboCount = 0;
        private const int MAX_COMBO = 4;

        public CharacterAnimationDriver(Animator animator)
        {
            _animator = animator ?? throw new System.ArgumentNullException(nameof(animator));
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

            // Sync JumpPhase
            if (_currentJumpPhase != data.JumpPhase)
            {
                _currentJumpPhase = data.JumpPhase;
                _animator.SetInteger(HASH_JumpPhase, (int)data.JumpPhase);
            }

            // Sync AttackPhase
            if (_currentAttackPhase != data.AttackPhase)
            {
                _currentAttackPhase = data.AttackPhase;
                _animator.SetInteger(HASH_AttackPhase, (int)data.AttackPhase);
            }
        }

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
        /// 开始跳跃 — 驱动 JumpStart 状态
        /// </summary>
        public void OnJumpStart()
        {
            _animator.SetBool(HASH_IsJumping, true);
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.Start);
            _currentJumpPhase = JumpPhase.Start;
            _animator.SetInteger(HASH_State, (int)CharacterState.JumpStart);
        }

        /// <summary>
        /// 跳跃过渡到空中（动画事件触发）
        /// </summary>
        public void OnJumpToAir()
        {
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.Air);
            _currentJumpPhase = JumpPhase.Air;
            _animator.SetInteger(HASH_State, (int)CharacterState.JumpAir);
        }

        /// <summary>
        /// 落地 — 驱动 JumpEnd 状态
        /// </summary>
        public void OnLanding()
        {
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.End);
            _currentJumpPhase = JumpPhase.End;
            _animator.SetInteger(HASH_State, (int)CharacterState.JumpEnd);
            _animator.SetBool(HASH_IsJumping, false);
        }

        /// <summary>
        /// 跳跃结束，回到 Idle 或 BattleIdle（动画事件触发）
        /// </summary>
        public void OnJumpEndComplete()
        {
            _currentJumpPhase = JumpPhase.None;
            _animator.SetInteger(HASH_JumpPhase, (int)JumpPhase.None);
            _animator.SetInteger(HASH_State, _isInCombat
                ? (int)CharacterState.BattleIdle
                : (int)CharacterState.Idle);
        }

        /// <summary>
        /// 开始攻击（支持地面和空中）
        /// </summary>
        public void OnAttack(int attackIndex)
        {
            if (attackIndex < 1 || attackIndex > MAX_COMBO) return;

            // Enter combat on first attack
            if (!_isInCombat)
                EnterBattle();

            _comboCount = attackIndex;
            _currentAttackPhase = (AttackPhase)attackIndex;

            _animator.SetInteger(HASH_SubState, attackIndex);
            _animator.SetInteger(HASH_AttackPhase, attackIndex);
        }

        /// <summary>
        /// 空中攻击（不打断跳跃状态，叠加动画）
        /// </summary>
        public void OnAttackInAir(int attackIndex)
        {
            OnAttack(attackIndex);
            // SubState drives Attack Layer which blends additively over JumpAir
        }

        /// <summary>
        /// 攻击完成（动画事件触发）
        /// </summary>
        public void OnAttackComplete()
        {
            _currentAttackPhase = AttackPhase.None;
            _animator.SetInteger(HASH_SubState, 0);
            _animator.SetInteger(HASH_AttackPhase, 0);
        }

        /// <summary>
        /// 尝试连击下一击（在 ComboWindowActive 期间调用）
        /// </summary>
        public void TryComboNext()
        {
            if (_comboCount < MAX_COMBO)
            {
                _comboCount++;
                OnAttack(_comboCount);
            }
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

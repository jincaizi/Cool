using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// 底层状态机 — 管理移动/跳跃/死亡
    /// </summary>
    public class BaseFSM
    {
        private readonly Animator _animator;
        private readonly StateTransitionTable _table;

        private BaseState _currentState;
        private BaseState? _lockedState;

        public BaseState CurrentState => _currentState;
        public event Action<BaseState> OnStateChanged;

        public BaseFSM(Animator animator, StateTransitionTable table)
        {
            _animator = animator;
            _table = table;
            _currentState = BaseState.Idle;
        }

        public void Update(CharacterData data, bool isAttacking)
        {
            if (_lockedState.HasValue)
            {
                if (_currentState != _lockedState.Value)
                    ForceState(_lockedState.Value);
                return;
            }

            // 使用内部 _currentState 而不是 data.BaseState 来评估转换
            // 这样避免外部修改 data.BaseState 导致的震荡

            // 如果当前处于攻击/技能状态，限制 BaseLayer 转换
            // 只允许跳跃相关转换，不允许 Idle/Move 之间的切换
            bool isInAttackState = isAttacking;

            var target = _table.Evaluate(_currentState, data);
            if (target.HasValue && target.Value != _currentState)
            {
                // 检查是否可以转换
                bool canTransition = _table.CanEnter(target.Value, data);

                // 如果处于攻击状态，只允许跳跃转换
                // 只允许跳跃转换，不允许 Locomotion 状态切换
                if (isInAttackState)
                {
                    // 允许 JumpStart/JumpAir/JumpEnd 转换
                    // 不允许 Idle/Move/Sprint/Locomotion 之间的切换
                    if (target.Value == BaseState.Idle ||
                        target.Value == BaseState.Move ||
                        target.Value == BaseState.Sprint ||
                        target.Value == BaseState.Locomotion)
                    {
                        canTransition = false;
                    }
                }

                if (canTransition)
                {
                    TransitionTo(target.Value);
                }
            }
        }

        public void ForceState(BaseState target)
        {
            if (_currentState != target)
            {
                _currentState = target;
                _animator.SetInteger(AnimHashes.BaseState, (int)target);
                OnStateChanged?.Invoke(target);
            }
        }

        public void LockState(BaseState state)
        {
            _lockedState = state;
            ForceState(state);
        }

        public void Unlock(BaseState defaultState = BaseState.Idle)
        {
            _lockedState = null;
            if (_currentState == BaseState.Death)
            {
                _currentState = defaultState;
                _animator.SetInteger(AnimHashes.BaseState, (int)_currentState);
            }
        }

        private void TransitionTo(BaseState target)
        {
            _currentState = target;
            _animator.SetInteger(AnimHashes.BaseState, (int)target);

            bool isJumping = target == BaseState.JumpStart
                          || target == BaseState.JumpAir
                          || target == BaseState.JumpEnd;
            _animator.SetBool(AnimHashes.IsJumping,isJumping);

            // 跳跃时将 Blend 设置为 0（停止 Locomotion 动画）
            if (isJumping)
            {
                _animator.SetFloat(AnimHashes.Blend, 0f);
            }

            OnStateChanged?.Invoke(target);
        }

        public void DebugSetState(BaseState state)
        {
            _currentState = state;
            _animator.SetInteger(AnimHashes.BaseState, (int)state);
        }
    }
}
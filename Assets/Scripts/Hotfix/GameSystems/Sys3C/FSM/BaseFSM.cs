using System;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Animation;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// 底层状态机 — 管理移动/跳跃/死亡
    /// </summary>
    public class BaseFSM
    {
        private readonly AnimationDriver _driver;
        private readonly StateTransitionTable _table;

        private BaseState _currentState;
        private BaseState? _lockedState;

        public BaseState CurrentState => _currentState;
        public event Action<BaseState> OnStateChanged;

        public BaseFSM(AnimationDriver driver, StateTransitionTable table)
        {
            _driver = driver;
            _table = table;
            _currentState = BaseState.Idle;
        }

        public void Update(CharacterData data, AttackState attackState)
        {
            if (_lockedState.HasValue)
            {
                if (_currentState != _lockedState.Value)
                    ForceState(_lockedState.Value);
                return;
            }

            var target = _table.Evaluate(_currentState, data);
            if (target.HasValue && target.Value != _currentState)
            {
                if (_table.CanEnter(target.Value, data))
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
                _driver.SetBaseState(target);
                OnStateChanged?.Invoke(target);
                UnityEngine.Debug.Log($"[BaseFSM] ForceState: {_currentState}");
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
                _driver.SetBaseState(_currentState);
            }
        }

        private void TransitionTo(BaseState target)
        {
            _currentState = target;
            _driver.SetBaseState(target);

            bool isJumping = target == BaseState.JumpStart
                          || target == BaseState.JumpAir
                          || target == BaseState.JumpEnd;
            _driver.SetIsJumping(isJumping);

            OnStateChanged?.Invoke(target);
            UnityEngine.Debug.Log($"[BaseFSM] Transition: {_currentState}");
        }
    }
}
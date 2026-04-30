using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Sys3C.Character;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// 状态转换条件委托
    /// </summary>
    public delegate bool TransitionCondition(CharacterData data);

    /// <summary>
    /// 单个转换规则
    /// </summary>
    public struct StateTransition
    {
        public BaseState TargetState;
        public TransitionCondition Condition;
        public float Priority;

        public StateTransition(BaseState target, TransitionCondition condition, float priority = 0)
        {
            TargetState = target;
            Condition = condition;
            Priority = priority;
        }
    }

    /// <summary>
    /// 状态转换表 — 外部化的状态规则配置
    /// </summary>
    public class StateTransitionTable
    {
        private readonly Dictionary<BaseState, List<StateTransition>> _transitions;

        public StateTransitionTable()
        {
            _transitions = new Dictionary<BaseState, List<StateTransition>>();
            Initialize();
        }

        private void Initialize()
        {
            // Idle
            _transitions[BaseState.Idle] = new List<StateTransition>
            {
                new StateTransition(BaseState.Move, d => d.MoveDir.sqrMagnitude > 0.01f, 1),
                new StateTransition(BaseState.Sprint, d => d.MoveDir.sqrMagnitude > 0.01f && d.IsSprint, 2),
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // Move
            _transitions[BaseState.Move] = new List<StateTransition>
            {
                new StateTransition(BaseState.Idle, d => d.MoveDir.sqrMagnitude < 0.01f, 1),
                new StateTransition(BaseState.Sprint, d => d.IsSprint, 2),
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // Sprint
            _transitions[BaseState.Sprint] = new List<StateTransition>
            {
                new StateTransition(BaseState.Idle, d => d.MoveDir.sqrMagnitude < 0.01f, 1),
                new StateTransition(BaseState.Move, d => !d.IsSprint, 2),
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpStart → JumpAir（自动）
            _transitions[BaseState.JumpStart] = new List<StateTransition>
            {
                new StateTransition(BaseState.JumpAir, d => true, 0),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpAir → JumpEnd（落地检测）
            _transitions[BaseState.JumpAir] = new List<StateTransition>
            {
                new StateTransition(BaseState.JumpEnd, d => d.IsGrounded && d.Velocity.y <= 0, 0),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpEnd
            _transitions[BaseState.JumpEnd] = new List<StateTransition>
            {
                new StateTransition(BaseState.Idle, d => true, 1),
                new StateTransition(BaseState.Move, d => d.MoveDir.sqrMagnitude > 0.01f, 2),
                new StateTransition(BaseState.Sprint, d => d.MoveDir.sqrMagnitude > 0.01f && d.IsSprint, 3)
            };

            // Death
            _transitions[BaseState.Death] = new List<StateTransition>();
        }

        public BaseState? Evaluate(BaseState currentFSMState, CharacterData data)
        {
            // 使用 CharacterData.BaseState 作为当前状态，而不是 FSM 内部状态
            BaseState currentDataState = data.BaseState;

            if (!_transitions.TryGetValue(currentDataState, out var transitions))
                return null;

            transitions.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            foreach (var t in transitions)
            {
                if (t.Condition(data))
                    return t.TargetState;
            }

            // 如果没有匹配的条件，保持当前状态
            return currentDataState;
        }

        public bool CanEnter(BaseState target, CharacterData data)
        {
            switch (target)
            {
                case BaseState.Idle:
                case BaseState.Move:
                case BaseState.Sprint:
                    return data.IsGrounded && !data.IsDead;

                case BaseState.JumpStart:
                    return data.IsGrounded && !data.IsDead && data.RequestJump;

                case BaseState.JumpAir:
                    return !data.IsDead;

                case BaseState.JumpEnd:
                    return !data.IsDead;

                case BaseState.Death:
                    return true;

                default:
                    return false;
            }
        }
    }
}
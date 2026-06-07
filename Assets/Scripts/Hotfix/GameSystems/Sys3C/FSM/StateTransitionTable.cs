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
            // ========== Locomotion 状态（Idle/Move/Sprint 的统一入口）==========
            // BaseState = 0, 1, 2 都进入 Locomotion，Blend 参数控制动画
            // 注意：使用 BaseState 值作为条件，而不是立即无条件转换
            _transitions[BaseState.Idle] = new List<StateTransition>
            {
                new StateTransition(BaseState.Locomotion, d => true, 0),  // 进入 Locomotion
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            _transitions[BaseState.Move] = new List<StateTransition>
            {
                new StateTransition(BaseState.Locomotion, d => true, 0),  // 进入 Locomotion
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            _transitions[BaseState.Sprint] = new List<StateTransition>
            {
                new StateTransition(BaseState.Locomotion, d => true, 0),  // 进入 Locomotion
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // ========== Locomotion 状态（Blend Tree）==========
            // 保持 Locomotion 状态，Blend 参数由代码控制
            _transitions[BaseState.Locomotion] = new List<StateTransition>
            {
                new StateTransition(BaseState.JumpStart, d => d.RequestJump, 10),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpStart → JumpAir（等待动画播放完成，约 0.9 后自动转换）
            _transitions[BaseState.JumpStart] = new List<StateTransition>
            {
                new StateTransition(BaseState.JumpAir, d => true, 0),  // 动画播放时自动转换
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpAir → JumpEnd（落地检测）
            _transitions[BaseState.JumpAir] = new List<StateTransition>
            {
                new StateTransition(BaseState.JumpEnd, d => d.IsGrounded && d.Velocity.y <= 0, 0),
                new StateTransition(BaseState.Death, d => d.IsDead, 100)
            };

            // JumpEnd → Locomotion（落地后返回 Locomotion 状态）
            _transitions[BaseState.JumpEnd] = new List<StateTransition>
            {
                new StateTransition(BaseState.Locomotion, d => true, 1)
            };

            // Death
            _transitions[BaseState.Death] = new List<StateTransition>();

            // ========== Defend 状态 ==========
            _transitions[BaseState.Defend] = new List<StateTransition>
            {
                new StateTransition(BaseState.Death, d => d.IsDead, 100),
                new StateTransition(BaseState.Move, d => d.MoveDir.sqrMagnitude > 0.01f, 2),
                new StateTransition(BaseState.Idle, d => true, 1)
            };
        }

        public BaseState? Evaluate(BaseState currentFSMState, CharacterData data)
        {
            // 使用 FSM 内部状态 currentFSMState 来评估转换
            // 不依赖 data.BaseState，避免外部修改导致的震荡
            if (!_transitions.TryGetValue(currentFSMState, out var transitions))
                return null;

            transitions.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            foreach (var t in transitions)
            {
                if (t.Condition(data))
                    return t.TargetState;
            }

            // 如果没有匹配的条件，保持当前状态
            return currentFSMState;
        }

        public bool CanEnter(BaseState target, CharacterData data)
        {
            switch (target)
            {
                case BaseState.Idle:
                case BaseState.Move:
                case BaseState.Sprint:
                    return data.IsGrounded && !data.IsDead;

                case BaseState.Locomotion:
                    // Locomotion 可以从任何状态进入（除了死亡）
                    return !data.IsDead;

                case BaseState.JumpStart:
                    return data.IsGrounded && !data.IsDead && data.RequestJump;

                case BaseState.JumpAir:
                    return !data.IsDead;

                case BaseState.JumpEnd:
                    return !data.IsDead;

                case BaseState.Death:
                    return true;

                case BaseState.Defend:
                    return data.IsGrounded && !data.IsDead;

                default:
                    return false;
            }
        }
    }
}
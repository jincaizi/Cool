using System.Collections.Generic;

namespace Hotfix.GameSystems.Monster
{
    // Core FSM that manages AI state lifecycle.
    //
    // States are created once and reused (zero allocation during gameplay).
    // Global transitions (Death) are checked before state-specific ones.
    // Multi-phase execution prevents double-OnUpdate bugs on transition frames.
    //
    // To add a state: create the class, then register it in the states dictionary
    // passed to the constructor.
    public class AIStateMachine
    {
        private readonly Dictionary<MonsterAIState, AIStateBase> _states;
        private AIStateBase _currentState;

        public MonsterAIState CurrentStateType => _currentState.StateType;

        // All state instances are passed in — AIStateMachine doesn't create them.
        // This allows states to have constructor dependencies (config, movement, etc.).
        public AIStateMachine(Dictionary<MonsterAIState, AIStateBase> states, MonsterAIState initialState)
        {
            _states = states;
            _currentState = _states[initialState];
        }

        // Must be called after construction, before the first Update.
        // Fires OnEnter for the initial state — sets up animator params, timers, etc.
        public void Initialize(AIContext ctx)
        {
            ctx.CurrentState = _currentState.StateType;
            _currentState.OnEnter(ctx);
        }

        public void EvaluateAndTransition(AIContext ctx)
        {
            var nextStateType = CheckGlobalTransitions(ctx)
                ?? _currentState.EvaluateTransitions(ctx);

            if (!nextStateType.HasValue) return;

            if (_states.TryGetValue(nextStateType.Value, out var nextState))
            {
                TransitionTo(nextState, ctx);
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[AIStateMachine] State {_currentState.StateType} wants to transition to {nextStateType.Value}, but that state is not registered");
            }
        }

        public void TransitionTo(AIStateBase nextState, AIContext ctx)
        {
            if (_currentState == nextState) return;

            _currentState.OnExit(ctx);
            ctx.PreviousState = _currentState.StateType;
            _currentState = nextState;
            ctx.CurrentState = nextState.StateType;
            nextState.OnEnter(ctx);
        }

        // Phase 4: Execute current state's per-frame logic.
        public void ExecuteState(AIContext ctx)
        {
            _currentState.OnUpdate(ctx);
        }

        // Force transition to a specific state, skipping EvaluateTransitions.
        // Used for externally-triggered transitions (Hit, Death).
        public void ForceState(MonsterAIState stateType, AIContext ctx)
        {
            if (_states.TryGetValue(stateType, out var state))
            {
                TransitionTo(state, ctx);
            }
            else
            {
                UnityEngine.Debug.LogError($"[AIStateMachine] ForceState: state {stateType} is not registered in the state dictionary");
            }
        }

        // Global transitions that can interrupt any state.
        // Death always takes priority. Future: boss phase transitions also go here.
        private MonsterAIState? CheckGlobalTransitions(AIContext ctx)
        {
            if (ctx.IsDead && _currentState.StateType != MonsterAIState.Death)
                return MonsterAIState.Death;
            return null;
        }
    }
}

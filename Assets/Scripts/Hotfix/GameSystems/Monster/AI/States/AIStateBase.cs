namespace Hotfix.GameSystems.Monster
{
    // Base class for all AI states.
    //
    // Lifecycle (called by AIStateMachine):
    //   1. OnExit(old state) → 2. swap current → 3. OnEnter(new state) → 4. OnUpdate(new state, next frame)
    //
    // IMPORTANT: States receive AIContext as a method parameter.
    // Do NOT store AIContext as a field — it contains Unity Object references
    // (Animator, Transform) that become invalid on GameObject destruction.
    // All state access must go through the method parameter, which is
    // guaranteed valid for the duration of the call.
    //
    // States are created once in AIStateMachine constructor and reused.
    // Each OnEnter must fully reset state (clear timers, reset flags).
    //
    // To add a new state:
    // 1. Create a subclass implementing these methods
    // 2. Register in AIStateMachine's state dictionary
    // 3. Done — no changes to AIBrain or other states needed
    public abstract class AIStateBase
    {
        // Called once when entering this state. Use for:
        // - Setting Animator parameters (triggers, bools, ints)
        // - Starting timers
        // - Initial movement commands (Stop, Resume, Chase)
        public virtual void OnEnter(AIContext ctx) { }

        // Called every frame while this state is active. Use for:
        // - Movement updates (LookAt, Chase destination)
        // - Mid-state logic (windup countdown, block tracking)
        // Do NOT transition out here — use EvaluateTransitions instead.
        public virtual void OnUpdate(AIContext ctx) { }

        // Called every frame. Return the next state to transition to,
        // or null to stay in this state. AIStateMachine evaluates
        // global transitions (Death) before calling this.
        public virtual MonsterAIState? EvaluateTransitions(AIContext ctx) { return null; }

        // Called once when leaving this state. Use for:
        // - Cleaning up Animator parameters
        // - Stopping timers
        // - Resetting movement flags
        public virtual void OnExit(AIContext ctx) { }

        // Unique identifier for this state, used for Animator hash lookup.
        public abstract MonsterAIState StateType { get; }
    }
}

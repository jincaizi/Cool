using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // AIContext is the single source of truth for all AI-related mutable state.
    // It is a class (not struct) so mutations persist across method calls.
    //
    // Field ownership conventions (enforced by convention, not compiler):
    //
    // ── Owned by AIBrain ──
    //   Target, DeltaTime, AttackCooldown
    //
    // ── Owned by individual States ──
    //   StateTimer (each state manages its own timer via OnUpdate)
    //
    // ── Owned by AIStateMachine ──
    //   CurrentState, PreviousState
    //
    // ── Owned by specific State ──
    //   BlockCount (DefendState)
    //
    // ── Owned by DamagePipeline ──
    //   LastHitResult, LastHitDirection, LastKnockbackForce
    //
    // Rule: Read any field. Write only your own fields.
    // If you find yourself writing another component's field, add a method
    // to that component instead.
    public class AIContext
    {
        // ── References (set once at construction, never null after Init) ──
        public Transform Self;
        public Animator Animator;
        public MonsterStats Stats;
        public MonsterMovement Movement;
        public MonsterConfig Config;

        // ── AIBrain-owned ──
        public Transform Target;
        public float DeltaTime;
        public float AttackCooldown;

        // ── AIStateMachine-owned ──
        public MonsterAIState CurrentState;
        public float StateTimer;
        public MonsterAIState PreviousState;

        // ── DefendState-owned ──
        public int BlockCount;

        // ── DamagePipeline-owned ──
        public DamageResult LastHitResult;
        public Vector3 LastHitDirection;
        public float LastKnockbackForce;

        public bool IsDead => Stats.IsDead;
    }
}

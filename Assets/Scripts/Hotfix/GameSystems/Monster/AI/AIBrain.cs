using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Monster
{
    // AIBrain is the single entry point for monster AI.
    // It owns the AIContext, AIStateMachine, and the main update loop.
    // MonsterEntity calls Update() each frame -- everything else is internal.
    //
    // Design principle: AIBrain knows nothing about specific state behavior.
    // Adding a new state requires zero changes to this class.
    public class AIBrain
    {
        private readonly AIContext _ctx;
        private readonly AIStateMachine _fsm;
        private readonly MonsterConfig _config;

        public MonsterAIState CurrentState => _ctx.CurrentState;
        public Vector3 LastHitDirection => _ctx.LastHitDirection;
        public float LastKnockbackForce => _ctx.LastKnockbackForce;

        public AIBrain(
            AIContext ctx,
            AIStateMachine fsm,
            MonsterConfig config)
        {
            _ctx = ctx;
            _fsm = fsm;
            _config = config;
        }

        // Must be called after construction, before first Update.
        // Fires OnEnter for the initial state.
        public void Initialize()
        {
            _fsm.Initialize(_ctx);
        }

        // Main update loop called by MonsterEntity each frame.
        public void Update(float deltaTime)
        {
            if (_ctx.IsDead) return;

            _ctx.DeltaTime = deltaTime;
            _ctx.AttackCooldown -= deltaTime;

            // Knockback physics runs regardless of AI state
            _ctx.Movement.UpdateKnockback(deltaTime);

            TryFindTarget();

            // Three-phase FSM execution:
            // 1. Evaluate transitions (may change state)
            _fsm.EvaluateAndTransition(_ctx);

            // 2. Execute current state (OnUpdate always runs once)
            _fsm.ExecuteState(_ctx);
        }

        private void TryFindTarget()
        {
            if (_ctx.Target != null) return;

            var players = PhysicsRegistry.Instance.FindNearby(
                _ctx.Self.position, _config.DetectRange, EntityType.Player);
            if (players.Count > 0)
                _ctx.Target = players[0].Transform;
        }

        // Called by MonsterEntity when a damage result is available from the pipeline.
        // Decides whether to enter Hit state based on HitReactLevel and current state.
        public void OnDamageReceived(DamageResult result, Vector3 hitDirection)
        {
            if (_ctx.IsDead) return;

            _ctx.LastHitResult = result;
            _ctx.LastHitDirection = hitDirection;
            _ctx.LastKnockbackForce = result.ShouldKnockback ? result.FinalDamage * 0.5f : 0f;

            // Defend: frontal blocks play a spark VFX instead of hit reaction
            if (_ctx.CurrentState == MonsterAIState.Defend && result.WasReduced)
            {
                _ctx.Animator.SetTrigger(MonsterAnimHashes.Hit);
                return;
            }

            // HitReactLevel None = ignore (boss super armor, i-frame active)
            if (result.ReactLevel == HitReactLevel.None)
                return;

            // Force transition to Hit state -- FSM handles OnExit(old) + OnEnter(Hit)
            _fsm.ForceState(MonsterAIState.Hit, _ctx);
        }

        // Called by MonsterEntity when HP reaches 0.
        public void EnterDeath()
        {
            _fsm.ForceState(MonsterAIState.Death, _ctx);
        }
    }
}

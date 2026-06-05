using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;

namespace Hotfix.GameSystems.Monster
{
    // Central damage processing pipeline.
    //
    // Flow: Modifier Chain → Gate Check → ApplyDamage
    //
    // Modifiers are sorted by priority (lowest first) and each gets a chance to reduce
    // or block damage before it reaches HP. The pipeline is synchronous and atomic per call.
    //
    // PostDamageNotify (VFX, events) is handled by the caller (MonsterEntity).
    // Death check is handled by MonsterStats.OnDeath event.
    //
    // To register additional modifiers: call AddModifier() in MonsterEntity.Init().
    public class DamagePipeline
    {
        private readonly MonsterConfig _config;
        private readonly MonsterStats _stats;
        private readonly List<IDamageModifier> _modifiers = new List<IDamageModifier>();
        private readonly IFrameModifier _iFrameModifier;
        private bool _sorted = true;

        public DamagePipeline(MonsterConfig config, MonsterStats stats)
        {
            _config = config;
            _stats = stats;
            _iFrameModifier = new IFrameModifier();
            _modifiers.Add(_iFrameModifier);
        }

        // Add a modifier. Called during initialization to register defense types.
        // Modifiers are sorted lazily — only when the list changes, not on every Process call.
        public void AddModifier(IDamageModifier modifier)
        {
            _modifiers.Add(modifier);
            _sorted = false;
        }

        // Enable/disable brief invincibility after being hit.
        // Called by HitState.OnEnter and HitState.OnExit.
        public void SetIFrameActive(bool active)
        {
            _iFrameModifier.Active = active;
        }

        // Entry point called from MonsterEntity.TakeDamage.
        // Returns the merged result — caller uses HitReactLevel and ShouldKnockback
        // to drive AI state transitions.
        public DamageResult Process(ref DamageContext ctx)
        {
            ctx.CurrentDamage = ctx.RawDamage;

            // Phase 1: Run all modifiers in priority order
            if (!_sorted)
            {
                _modifiers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                _sorted = true;
            }

            var merged = new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            foreach (var modifier in _modifiers)
            {
                var result = modifier.Modify(ref ctx);
                // Always propagate ShouldKnockback and ReactLevel — modifiers may
                // override these even without reducing damage (e.g., stagger immunity).
                merged.ShouldKnockback = result.ShouldKnockback;
                merged.ReactLevel = result.ReactLevel;

                if (result.WasBlocked)
                {
                    merged.WasBlocked = true;
                    merged.FinalDamage = 0;
                    break;
                }
                if (result.WasReduced)
                {
                    merged.WasReduced = true;
                    merged.FinalDamage = result.FinalDamage;
                }
            }

            // Phase 2: Gate check — if blocked, skip damage but still notify
            if (merged.WasBlocked)
                return merged;

            // Phase 3: Apply damage
            if (_stats.IsDead)
                return merged;

            _stats.ApplyDamage(merged.FinalDamage);

            return merged;
        }
    }
}

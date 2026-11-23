using System;
using System.Collections.Generic;
using Cool.SyncCore;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.VFX;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    /// <summary>把共享状态运行时接到单机时钟、怪物 DoT 扣血和状态表现。</summary>
    internal sealed class OfflineMonsterStatuses
    {
        private readonly StatusRuntime _runtime = new StatusRuntime();
        private readonly List<ActiveStatus> _dueDots = new List<ActiveStatus>();
        private readonly List<int> _visibleEffects = new List<int>();
        private readonly Transform _target;
        private readonly int _entityId;
        private readonly Action<float> _applyDot;
        private uint _tick;

        internal float SpeedScale { get; private set; } = 1f;
        internal bool ActionLock { get; private set; }
        internal float IncomingDamageMultiplier => _runtime.IncomingDamageMultiplier(false, 1f);
        internal static uint LocalTick => (uint)(Time.timeAsDouble * CombatRules.TickRateHz);

        internal OfflineMonsterStatuses(Transform target, int entityId, Action<float> applyDot)
        {
            _target = target;
            _entityId = entityId;
            _applyDot = applyDot;
        }

        internal void Apply(ElementDefinition element, OfflineWeaponCast cast, uint tick, bool armoredWindup)
        {
            AdvanceTo(tick);
            foreach (int effectId in element.StatusEffectIds)
            {
                var definition = ElementalCatalogDefaults.FindStatus(effectId);
                if (definition == null || (armoredWindup
                    && definition.EffectKind == (int)StatusEffectKind.Paralyze))
                    continue;

                var applied = _runtime.Apply(definition, cast.SourceId, cast.SkillId,
                    cast.CastId, tick, targetIsPlayer: false, sourceIsPlayer: true);
                if (applied != StatusApplyResult.Applied) continue;

                RemoveExpiredVisuals();
                _visibleEffects.Add(effectId);
                EventBus.Emit(new StatusEffectAppliedEvent
                {
                    EntityId = _entityId,
                    Target = _target,
                    EffectType = AuthoritativeStatusVfx.Map(effectId),
                    EffectId = effectId.ToString(),
                    Duration = definition.DurationSeconds,
                    Stacks = 1,
                });
            }
            DeriveControl();
        }

        internal void AdvanceTo(uint tick)
        {
            if (tick <= _tick) return;
            int previousCount = _runtime.Count;
            while (_tick < tick && _runtime.Count > 0)
            {
                ++_tick;
                // 与服务端同序。已到期的风不放大该 tick 的灼烧，末跳在到期移除前结算。
                _runtime.RemoveExpiredModifiers(_tick);
                _runtime.CollectDueDots(_tick, _dueDots);
                foreach (var status in _dueDots)
                {
                    _applyDot(status.Definition.DamagePerTick * IncomingDamageMultiplier);
                    if (_runtime.Count == 0) break; // 扣血引发死亡时会同步 Clear。
                    _runtime.AdvanceDot(status, _tick);
                }
                _dueDots.Clear();
                _runtime.RemoveExpiredDots(_tick);
            }
            _tick = tick;

            if (_runtime.Count == previousCount) return;
            DeriveControl();
            RemoveExpiredVisuals();
        }

        internal void Clear()
        {
            _runtime.ClearAll();
            DeriveControl();
            RemoveExpiredVisuals();
        }

        private void DeriveControl()
        {
            _runtime.DeriveControl(out float speedScale, out bool actionLock);
            SpeedScale = speedScale;
            ActionLock = actionLock;
        }

        private void RemoveExpiredVisuals()
        {
            for (int i = _visibleEffects.Count - 1; i >= 0; i--)
            {
                int effectId = _visibleEffects[i];
                if (_runtime.Contains(effectId)) continue;

                _visibleEffects.RemoveAt(i);
                EventBus.Emit(new StatusEffectRemovedEvent
                {
                    EntityId = _entityId,
                    Target = _target,
                    EffectType = AuthoritativeStatusVfx.Map(effectId),
                    EffectId = effectId.ToString(),
                });
            }
        }
    }
}

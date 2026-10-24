using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    public sealed class StatusEffectRegistry
    {
        private sealed class ActiveEffect
        {
            public IEffectTarget Target;
            public GameObject TargetGo;
            public IEffectTarget Caster;
            public GameObject CasterGo;
            public EffectData Effect;
            public float Remaining;
            public float TimeToNextTick;
            public int Stacks;
        }

        private readonly List<ActiveEffect> _active = new();

        public void Apply(IEffectTarget target, EffectData effect, IEffectTarget caster)
        {
            if (target == null || effect == null) return;

            GameObject targetGo = GetGameObject(target);
            if (targetGo == null) return;
            if (target is IDamageable damageable && !damageable.IsAlive) return;

            if (effect.Duration <= 0f)
            {
                effect.Apply(caster, target);
                return;
            }

            int targetId = targetGo.GetInstanceID();
            ActiveEffect existing = Find(targetId, GetKey(effect));

            if (existing != null)
            {
                switch (effect.StackingRule)
                {
                    case StackingRule.Ignore:
                        return;
                    case StackingRule.Stack:
                        existing.Stacks = Mathf.Min(
                            existing.Stacks + 1, Mathf.Max(1, effect.MaxStacks));
                        Refresh(existing, effect, caster);
                        return;
                    default:
                        Refresh(existing, effect, caster);
                        return;
                }
            }

            effect.Apply(caster, target);

            _active.Add(new ActiveEffect
            {
                Target = target,
                TargetGo = targetGo,
                Caster = caster,
                CasterGo = GetGameObject(caster),
                Effect = effect,
                Remaining = effect.Duration,
                TimeToNextTick = effect.IsTickEffect ? effect.TickInterval : 0f,
                Stacks = 1,
            });

            EventBus.Emit(new StatusEffectAppliedEvent
            {
                EntityId = targetId,
                Target = target.transform,
                EffectType = effect.Type,
                EffectId = effect.EffectId,
                Duration = effect.Duration,
                Stacks = 1,
            });
        }

        public void Tick(float deltaTime)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveEffect entry = _active[i];

                if (entry.TargetGo == null)
                {
                    _active.RemoveAt(i);
                    continue;
                }

                if (entry.CasterGo == null)
                    entry.Caster = null;

                if (IsDead(entry.Target))
                {
                    RemoveAt(i);
                    continue;
                }

                float step = Mathf.Min(deltaTime, entry.Remaining);
                entry.Remaining -= deltaTime;

                if (entry.Effect.IsTickEffect && entry.Effect.TickInterval > 0f)
                {
                    entry.TimeToNextTick -= step;
                    while (entry.TimeToNextTick <= 0f)
                    {
                        entry.Effect.OnTick(entry.Caster, entry.Target, entry.Stacks);
                        entry.TimeToNextTick += entry.Effect.TickInterval;

                        if (IsDead(entry.Target))
                            break;
                    }
                }

                if (IsDead(entry.Target) || entry.Remaining <= 0f)
                    RemoveAt(i);
            }
        }

        public void Remove(IEffectTarget target, string effectId)
        {
            GameObject targetGo = GetGameObject(target);
            if (targetGo == null) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].TargetGo == targetGo && _active[i].Effect.EffectId == effectId)
                    RemoveAt(i);
            }
        }

        public void RemoveAll(IEffectTarget target)
        {
            GameObject targetGo = GetGameObject(target);
            if (targetGo == null) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].TargetGo == targetGo)
                    RemoveAt(i);
            }
        }

        private void Refresh(ActiveEffect entry, EffectData effect, IEffectTarget caster)
        {
            entry.Remaining = effect.Duration;
            entry.TimeToNextTick = effect.IsTickEffect ? effect.TickInterval : 0f;
            entry.Caster = caster;
            entry.CasterGo = GetGameObject(caster);
        }

        private void RemoveAt(int index)
        {
            ActiveEffect entry = _active[index];

            entry.Effect.Remove(entry.Caster, entry.Target);

            EventBus.Emit(new StatusEffectRemovedEvent
            {
                EntityId = entry.TargetGo.GetInstanceID(),
                Target = entry.Target.transform,
                EffectType = entry.Effect.Type,
                EffectId = entry.Effect.EffectId,
            });

            _active.RemoveAt(index);
        }

        private ActiveEffect Find(int targetId, string key)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                ActiveEffect entry = _active[i];
                if (entry.TargetGo == null) continue;
                if (entry.TargetGo.GetInstanceID() != targetId) continue;
                if (GetKey(entry.Effect) == key) return entry;
            }
            return null;
        }

        private static string GetKey(EffectData effect)
        {
            return string.IsNullOrEmpty(effect.EffectId)
                ? RuntimeHelpers.GetHashCode(effect).ToString()
                : effect.EffectId;
        }

        private static GameObject GetGameObject(IEffectTarget target)
        {
            if (target == null) return null;
            if (target is Component component) return component.gameObject;
            Transform t = target.transform;
            return t != null ? t.gameObject : null;
        }

        private static bool IsDead(IEffectTarget target)
        {
            return target is IDamageable damageable && !damageable.IsAlive;
        }
    }
}

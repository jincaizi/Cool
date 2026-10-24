using System.Collections.Generic;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class StatusVFXSystem : MonoBehaviour
    {
        private sealed class TargetTint
        {
            public Transform Target;
            public BodyTintRenderer Tint;
            public int FreezeCount;
            public int BurnCount;
            public float Phase;
        }

        private static readonly Color FreezeColor = new Color(0.55f, 0.75f, 1f);
        private static readonly Color BurnColor = new Color(1f, 0.35f, 0.1f);

        private readonly Dictionary<int, TargetTint> _tints = new();
        private readonly List<int> _dead = new();

        private void OnEnable()
        {
            EventBus.Subscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Subscribe<StatusEffectRemovedEvent>(OnRemoved);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Unsubscribe<StatusEffectRemovedEvent>(OnRemoved);

            foreach (var state in _tints.Values)
            {
                // 目标销毁后其 Renderer 一并销毁，此时 SetPropertyBlock 会抛 MissingReferenceException
                if (state.Target != null)
                    state.Tint.Clear();
            }

            _tints.Clear();
        }

        private void OnApplied(StatusEffectAppliedEvent e)
        {
            if (e.Target == null) return;
            if (e.EffectType != EffectType.Freeze && e.EffectType != EffectType.Burn) return;

            if (!_tints.TryGetValue(e.EntityId, out TargetTint state))
            {
                state = new TargetTint
                {
                    Target = e.Target,
                    Tint = new BodyTintRenderer(e.Target.GetComponentsInChildren<Renderer>()),
                };
                _tints.Add(e.EntityId, state);
            }

            if (e.EffectType == EffectType.Freeze)
                state.FreezeCount++;
            else
                state.BurnCount++;
        }

        private void OnRemoved(StatusEffectRemovedEvent e)
        {
            if (e.EffectType != EffectType.Freeze && e.EffectType != EffectType.Burn) return;
            if (!_tints.TryGetValue(e.EntityId, out TargetTint state)) return;

            if (e.EffectType == EffectType.Freeze)
                state.FreezeCount = Mathf.Max(0, state.FreezeCount - 1);
            else
                state.BurnCount = Mathf.Max(0, state.BurnCount - 1);

            if (state.FreezeCount == 0 && state.BurnCount == 0)
            {
                state.Tint.Clear();
                _tints.Remove(e.EntityId);
            }
        }

        private void Update()
        {
            if (_tints.Count == 0) return;

            float dt = Time.deltaTime;
            _dead.Clear();

            foreach (var pair in _tints)
            {
                TargetTint state = pair.Value;

                if (state.Target == null)
                {
                    // 目标 GameObject 销毁时 Renderer 已不存在，无需（也无法）清理染色
                    _dead.Add(pair.Key);
                    continue;
                }

                if (state.FreezeCount > 0)
                {
                    state.Tint.Apply(FreezeColor, 0.5f, FreezeColor, 0f);
                }
                else if (state.BurnCount > 0)
                {
                    state.Phase += dt * Mathf.PI * 4f;
                    float amount = 0.12f + 0.18f * (0.5f + 0.5f * Mathf.Sin(state.Phase));
                    state.Tint.Apply(BurnColor, amount, BurnColor, 0f);
                }
                else
                {
                    state.Tint.Clear();
                }
            }

            for (int i = 0; i < _dead.Count; i++)
                _tints.Remove(_dead[i]);
        }
    }
}

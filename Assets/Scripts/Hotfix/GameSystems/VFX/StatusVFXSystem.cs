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
            public int StunCount;
            public int VulnerableCount;
            public float Phase;
            public ElementVfxAttachment Visual;

            public EffectType? DominantEffect
                => FreezeCount > 0 ? EffectType.Freeze :
                   StunCount > 0 ? EffectType.Stun :
                   BurnCount > 0 ? EffectType.Burn :
                   VulnerableCount > 0 ? EffectType.Vulnerable : (EffectType?)null;

            public void Clear()
            {
                Visual?.Clear();
                if (Target != null)
                    Tint.Clear();
            }
        }

        private static readonly Color FreezeColor = new Color(0.55f, 0.75f, 1f);
        private static readonly Color BurnColor = new Color(1f, 0.35f, 0.1f);
        private static readonly Color StunColor = new Color(0.7f, 0.4f, 1f);
        private static readonly Color VulnerableColor = new Color(0.3f, 1f, 0.9f);

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
                state.Clear();

            _tints.Clear();
        }

        /// <summary>冰/火/雷/风四状态参与染色；其余类型不进表。</summary>
        private static bool IsTinted(EffectType type)
            => type is EffectType.Freeze or EffectType.Burn
                or EffectType.Stun or EffectType.Vulnerable;

        private void OnApplied(StatusEffectAppliedEvent e)
        {
            if (e.Target == null) return;
            if (!IsTinted(e.EffectType)) return;

            if (!_tints.TryGetValue(e.EntityId, out TargetTint state) || state.Target != e.Target)
            {
                state?.Clear();
                state = new TargetTint
                {
                    Target = e.Target,
                    Tint = new BodyTintRenderer(e.Target.GetComponentsInChildren<Renderer>()),
                    Visual = Application.isPlaying ? new ElementVfxAttachment() : null,
                };
                _tints[e.EntityId] = state;
            }

            switch (e.EffectType)
            {
                case EffectType.Freeze: state.FreezeCount++; break;
                case EffectType.Burn: state.BurnCount++; break;
                case EffectType.Stun: state.StunCount++; break;
                case EffectType.Vulnerable: state.VulnerableCount++; break;
                // IsTinted 已过滤其余类型；显式列举，未来新增类型不会被静默计成 Vulnerable
            }
            UpdateVisual(state);
        }

        private void OnRemoved(StatusEffectRemovedEvent e)
        {
            if (!IsTinted(e.EffectType)) return;
            if (!_tints.TryGetValue(e.EntityId, out TargetTint state)) return;
            if (!ReferenceEquals(e.Target, null) && !ReferenceEquals(state.Target, e.Target)) return;

            switch (e.EffectType)
            {
                case EffectType.Freeze: state.FreezeCount = Mathf.Max(0, state.FreezeCount - 1); break;
                case EffectType.Burn: state.BurnCount = Mathf.Max(0, state.BurnCount - 1); break;
                case EffectType.Stun: state.StunCount = Mathf.Max(0, state.StunCount - 1); break;
                case EffectType.Vulnerable:
                    state.VulnerableCount = Mathf.Max(0, state.VulnerableCount - 1);
                    break;
            }

            if (state.DominantEffect == null)
            {
                state.Clear();
                _tints.Remove(e.EntityId);
            }
            else
            {
                UpdateVisual(state);
            }
        }

        private static void UpdateVisual(TargetTint state)
        {
            if (state.DominantEffect is EffectType effect)
                state.Visual?.Show(ElementVfxAssets.StatusKey(effect), state.Target);
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
                    state.Clear();
                    _dead.Add(pair.Key);
                    continue;
                }

                if (state.Visual?.IsVisible == true)
                {
                    // Clear only if there was an active fallback tint.
                    state.Tint.Apply(default, 0f, default, 0f);
                }
                else if (state.FreezeCount > 0)
                {
                    state.Tint.Apply(FreezeColor, 0.5f, FreezeColor, 0f);
                }
                else if (state.StunCount > 0)
                {
                    state.Tint.Apply(StunColor, 0.5f, StunColor, 0f);
                }
                else if (state.BurnCount > 0)
                {
                    state.Phase += dt * Mathf.PI * 4f;
                    float amount = 0.12f + 0.18f * (0.5f + 0.5f * Mathf.Sin(state.Phase));
                    state.Tint.Apply(BurnColor, amount, BurnColor, 0f);
                }
                else if (state.VulnerableCount > 0)
                {
                    state.Tint.Apply(VulnerableColor, 0.4f, VulnerableColor, 0f);
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

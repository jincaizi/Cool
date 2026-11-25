using System.Collections.Generic;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class StatusVFXSystem : MonoBehaviour
    {
        [SerializeField] private GameObject _freezeVisual;
        [SerializeField] private GameObject _burnVisual;
        [SerializeField] private GameObject _stunVisual;
        [SerializeField] private GameObject _vulnerableVisual;

        private sealed class TargetTint
        {
            public Transform Target;
            public BodyTintRenderer Tint;
            public readonly int[] Counts = new int[4];
            public readonly GameObject[] Visuals = new GameObject[4];
            public int ActiveTypes;
            public bool HasTint;
            public float Phase;
            public ElementVfxAttachment Visual;

            public EffectType? DominantEffect
                => Counts[VisualIndex(EffectType.Freeze)] > 0 ? EffectType.Freeze :
                   Counts[VisualIndex(EffectType.Stun)] > 0 ? EffectType.Stun :
                   Counts[VisualIndex(EffectType.Burn)] > 0 ? EffectType.Burn :
                   Counts[VisualIndex(EffectType.Vulnerable)] > 0 ? EffectType.Vulnerable : (EffectType?)null;

            public void Clear()
            {
                Visual?.Clear();
                if (Target != null && HasTint)
                    Tint.Clear();
                HasTint = false;
                for (int i = 0; i < Visuals.Length; i++)
                {
                    RemoveVisual(Visuals[i]);
                    Visuals[i] = null;
                }
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

        private void OnApplied(StatusEffectAppliedEvent e)
        {
            if (e.Target == null) return;
            int type = VisualIndex(e.EffectType);
            if (type < 0) return;

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

            if (state.Counts[type]++ == 0)
            {
                state.ActiveTypes++;
                var prefab = type switch
                {
                    0 => _freezeVisual,
                    1 => _burnVisual,
                    2 => _stunVisual,
                    _ => _vulnerableVisual
                };
                if (prefab != null)
                    state.Visuals[type] = Instantiate(prefab, state.Target, false);
            }
            UpdateVisual(state);
        }

        private void OnRemoved(StatusEffectRemovedEvent e)
        {
            int type = VisualIndex(e.EffectType);
            if (type < 0 || !_tints.TryGetValue(e.EntityId, out TargetTint state)) return;
            if (e.Target != null && e.Target != state.Target) return;
            if (state.Counts[type] == 0) return;

            if (--state.Counts[type] == 0)
            {
                RemoveVisual(state.Visuals[type]);
                state.Visuals[type] = null;
                state.ActiveTypes--;
            }

            if (state.ActiveTypes == 0)
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

                if (state.Target == null || !state.Target.gameObject.activeInHierarchy)
                {
                    state.Clear();
                    _dead.Add(pair.Key);
                    continue;
                }

                if (state.Visual?.IsVisible == true)
                {
                    // Clear only if there was an active fallback tint.
                    state.Tint.Apply(default, 0f, default, 0f);
                    state.HasTint = false;
                }
                else if (state.Counts[VisualIndex(EffectType.Freeze)] > 0)
                {
                    state.Tint.Apply(FreezeColor, 0.5f, FreezeColor, 0f);
                    state.HasTint = true;
                }
                else if (state.Counts[VisualIndex(EffectType.Stun)] > 0)
                {
                    state.Tint.Apply(StunColor, 0.5f, StunColor, 0f);
                    state.HasTint = true;
                }
                else if (state.Counts[VisualIndex(EffectType.Burn)] > 0)
                {
                    state.Phase += dt * Mathf.PI * 4f;
                    float amount = 0.12f + 0.18f * (0.5f + 0.5f * Mathf.Sin(state.Phase));
                    state.Tint.Apply(BurnColor, amount, BurnColor, 0f);
                    state.HasTint = true;
                }
                else if (state.Counts[VisualIndex(EffectType.Vulnerable)] > 0)
                {
                    state.Tint.Apply(VulnerableColor, 0.4f, VulnerableColor, 0f);
                    state.HasTint = true;
                }
                else if (state.HasTint)
                {
                    state.Tint.Clear();
                    state.HasTint = false;
                }
            }

            for (int i = 0; i < _dead.Count; i++)
                _tints.Remove(_dead[i]);
        }

        /// <summary>冰/火/雷/风四状态参与染色与预制体特效；其余类型不进表。</summary>
        private static int VisualIndex(EffectType type) => type switch
        {
            EffectType.Freeze => 0,
            EffectType.Burn => 1,
            EffectType.Stun => 2,
            EffectType.Vulnerable => 3,
            _ => -1
        };

        private static void RemoveVisual(GameObject visual)
        {
            if (visual == null) return;
            visual.SetActive(false);
            if (Application.isPlaying) Destroy(visual);
            else DestroyImmediate(visual);
        }
    }
}

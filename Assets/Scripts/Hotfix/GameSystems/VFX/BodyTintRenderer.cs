using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>用 MaterialPropertyBlock 给一组渲染器做身体染色，不污染共享材质。</summary>
    public sealed class BodyTintRenderer
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Renderer[] _renderers;
        private readonly Color[] _originals;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private bool _tinted;

        public BodyTintRenderer(Renderer[] renderers)
        {
            var valid = new List<Renderer>();
            var originals = new List<Color>();
            if (renderers != null)
            {
                foreach (var renderer in renderers)
                {
                    if (renderer == null || renderer.sharedMaterial == null
                        || !renderer.sharedMaterial.HasProperty(ColorId))
                        continue;
                    valid.Add(renderer);
                    originals.Add(renderer.sharedMaterial.GetColor(ColorId));
                }
            }
            _renderers = valid.ToArray();
            _originals = originals.ToArray();
        }

        public void Apply(Color lowColor, float lowAmount, Color hitColor, float hitAmount)
        {
            bool any = lowAmount > 0.001f || hitAmount > 0.001f;
            if (!any)
            {
                if (_tinted) Clear();
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                Color c = Color.Lerp(_originals[i], lowColor, Mathf.Clamp01(lowAmount));
                c = Color.Lerp(c, hitColor, Mathf.Clamp01(hitAmount));
                _block.SetColor(ColorId, c);
                _renderers[i].SetPropertyBlock(_block);
            }
            _tinted = true;
        }

        public void Clear()
        {
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].SetPropertyBlock(null);
            _tinted = false;
        }
    }
}

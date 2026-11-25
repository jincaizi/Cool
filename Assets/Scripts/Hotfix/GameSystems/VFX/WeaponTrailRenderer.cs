using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponTrailRenderer : MonoBehaviour
    {
        [SerializeField] private Material _texturedTrailMaterial;
        private TrailRenderer _trail;
        private Material _trailMaterial;
        private GameObject _childGo;

        public void Init(WeaponElementConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[WeaponTrailRenderer] Init called with null config");
                return;
            }
            if (_trail != null) return;

            _childGo = new GameObject("_weaponTrail");
            _childGo.transform.SetParent(transform, false);
            _childGo.transform.localPosition = WeaponElementConfig.GetAxisVector(config.Axis) * config.BladeOffset;
            _trail = _childGo.AddComponent<TrailRenderer>();

            bool textured = _texturedTrailMaterial != null;
            _trail.time = config.TrailTime * (textured ? 1.25f : 1f);
            _trail.minVertexDistance = textured ? config.TrailMinVertexDistance * 0.55f : config.TrailMinVertexDistance;
            if (textured)
            {
                _trail.widthMultiplier = config.TrailWidth * 1.85f;
                _trail.widthCurve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.36f, 0.9f),
                    new Keyframe(0.75f, 0.42f), new Keyframe(1f, 0f));
            }
            else
            {
                _trail.startWidth = config.TrailWidth;
                _trail.endWidth = 0f;
            }
            _trail.emitting = false;
            _trail.textureMode = LineTextureMode.Stretch;
            _trail.numCornerVertices = textured ? 5 : 3;

            var gradient = new Gradient();
            var c = config.TrailColor;
            if (textured)
            {
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.Lerp(Color.white, c, 0.12f), 0f),
                        new GradientColorKey(Color.Lerp(Color.white, c, 0.42f), 0.38f),
                        new GradientColorKey(c, 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.24f),
                        new GradientAlphaKey(0.38f, 0.65f), new GradientAlphaKey(0f, 1f)
                    });
            }
            else
            {
                gradient.SetKeys(
                    new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            }
            _trail.colorGradient = gradient;

            if (_texturedTrailMaterial != null)
                _trail.sharedMaterial = _texturedTrailMaterial;
            else
            {
                var shader = Shader.Find("Particles/Standard Unlit");
                if (shader != null)
                {
                    _trailMaterial = new Material(shader);
                    _trailMaterial.SetInt("_BlendOp", 0);
                    _trailMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _trailMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    _trailMaterial.SetInt("_ZWrite", 0);
                    _trailMaterial.EnableKeyword("_EMISSION");
                    _trailMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    _trailMaterial.SetColor("_EmissionColor", c);
                    _trail.sharedMaterial = _trailMaterial;
                }
            }

            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
        }

        private void OnDisable()
        {
            if (_trail == null) return;
            _trail.emitting = false;
            ClearHistory();
        }

        internal void ClearHistory() => _trail?.Clear();

        public void SetEmitting(bool emitting)
        {
            if (_trail != null)
                _trail.emitting = emitting;
        }

        private void OnDestroy()
        {
            if (_trailMaterial != null)
                Destroy(_trailMaterial);
            if (_childGo != null)
                Destroy(_childGo);
        }
    }
}

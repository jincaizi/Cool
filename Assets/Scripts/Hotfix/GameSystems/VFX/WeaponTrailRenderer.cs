using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponTrailRenderer : MonoBehaviour
    {
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

            _trail.time = config.TrailTime;
            _trail.minVertexDistance = config.TrailMinVertexDistance;
            _trail.startWidth = config.TrailWidth;
            _trail.endWidth = 0f;
            _trail.emitting = false;

            var gradient = new Gradient();
            var c = config.TrailColor;
            gradient.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(new Color(c.r, c.g, c.b, 0f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = gradient;

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
                _trail.material = _trailMaterial;
            }

            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
        }

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

using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponMistParticles : MonoBehaviour
    {
        private GameObject _childGo;
        private ParticleSystem _ps;
        private Material _cachedMaterial;
        private ParticleSystem.EmissionModule _emission;
        private ParticleSystem.MainModule _main;
        private ParticleSystem.ShapeModule _shape;
        private ParticleSystem.VelocityOverLifetimeModule _velOverLifetime;
        private ParticleSystem.NoiseModule _noise;
        private ParticleSystem.ColorOverLifetimeModule _colorOverLifetime;

        public void Init(WeaponElementConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[WeaponMistParticles] Init called with null config");
                return;
            }

            if (_ps != null)
                return;

            _childGo = new GameObject("_weaponMistParticles");
            _childGo.transform.SetParent(transform, false);
            _childGo.transform.localPosition = WeaponElementConfig.GetAxisVector(config.Axis) * config.BladeOffset;
            _ps = _childGo.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _main = _ps.main;
            _main.startLifetime = new ParticleSystem.MinMaxCurve(config.MistLifetimeMin, config.MistLifetimeMax);
            _main.startSize = new ParticleSystem.MinMaxCurve(config.MistStartSizeMin, config.MistStartSizeMax);
            _main.startSpeed = 0f;
            _main.simulationSpace = ParticleSystemSimulationSpace.Local;
            _main.startColor = config.MistStartColor;
            int maxP = config.Quality == VFXQualityLevel.Low ? config.MaxParticlesLow : config.MaxParticlesHigh;
            _main.maxParticles = maxP;
            _main.duration = 999f;
            _main.loop = true;

            _emission = _ps.emission;
            float rate = config.Quality == VFXQualityLevel.Low ? config.EmissionRateLow : config.MistEmissionRate;
            _emission.rateOverTime = rate;

            _shape = _ps.shape;
            _shape.shapeType = (ParticleSystemShapeType)17; // Cylinder
            _shape.radius = config.MistShapeRadius;
            _shape.radiusThickness = 0.3f;
            _shape.arc = 360f;
            _shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
            _shape.scale = new Vector3(1f, config.MistShapeHeight / 2f, 1f);

            _velOverLifetime = _ps.velocityOverLifetime;
            _velOverLifetime.enabled = true;
            // All axes must share the same curve mode — set linear X/Y/Z explicitly
            _velOverLifetime.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            _velOverLifetime.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            _velOverLifetime.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            _velOverLifetime.orbitalZ = new ParticleSystem.MinMaxCurve(config.MistOrbitalSpeedMin, config.MistOrbitalSpeedMax);
            _velOverLifetime.radial = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            _velOverLifetime.space = ParticleSystemSimulationSpace.Local;

            _colorOverLifetime = _ps.colorOverLifetime;
            _colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(config.MistStartColor, 0f), new GradientColorKey(config.MistStartColor, 0.3f),
                        new GradientColorKey(config.MistEndColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.3f), new GradientAlphaKey(0f, 1f) });
            _colorOverLifetime.color = grad;

            _noise = _ps.noise;
            _noise.enabled = true;
            _noise.strength = config.MistNoiseStrength;
            _noise.frequency = config.MistNoiseFrequency;
            _noise.scrollSpeed = 0.3f;

            var renderer = _childGo.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _cachedMaterial = GetDefaultAdditiveMaterial();
            if (config.ParticleTexture != null)
                _cachedMaterial.mainTexture = config.ParticleTexture;
            renderer.material = _cachedMaterial;

            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetVisible(bool visible)
        {
            if (_ps == null) return;
            if (visible && !_ps.isPlaying)
                _ps.Play();
            else if (!visible && _ps.isPlaying)
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void OnDestroy()
        {
            if (_cachedMaterial != null)
            {
                Destroy(_cachedMaterial);
                _cachedMaterial = null;
            }

            if (_childGo != null)
            {
                Destroy(_childGo);
                _childGo = null;
            }
        }

        private static Material GetDefaultAdditiveMaterial()
        {
            var mat = new Material(Shader.Find("Particles/Standard Unlit"));
            mat.SetInt("_BlendOp", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.color = Color.white;
            mat.SetColor("_EmissionColor", Color.white);
            return mat;
        }
    }
}

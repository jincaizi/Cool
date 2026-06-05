using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponMistParticles : MonoBehaviour
    {
        [SerializeField] private GameObject _mistPrefab;

        private GameObject _childGo;
        private ParticleSystem _ps;
        private Material _cachedMaterial;

        public void Init(WeaponElementConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[WeaponMistParticles] Init called with null config");
                return;
            }

            if (_ps != null)
                return;

            if (_mistPrefab == null)
            {
                Debug.LogError("[WeaponMistParticles] _mistPrefab is not assigned");
                return;
            }

            _childGo = Instantiate(_mistPrefab, transform);
            _childGo.name = "_weaponMistParticles";
            _childGo.transform.localPosition = WeaponElementConfig.GetAxisVector(config.Axis) * config.BladeOffset;
            _ps = _childGo.GetComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Override config-dependent properties
            var main = _ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(config.MistLifetimeMin, config.MistLifetimeMax);
            main.startSize = new ParticleSystem.MinMaxCurve(config.MistStartSizeMin, config.MistStartSizeMax);
            main.startColor = config.MistStartColor;
            main.maxParticles = config.Quality == VFXQualityLevel.Low ? config.MaxParticlesLow : config.MaxParticlesHigh;

            var emission = _ps.emission;
            emission.rateOverTime = config.Quality == VFXQualityLevel.Low ? config.EmissionRateLow : config.MistEmissionRate;

            var shape = _ps.shape;
            shape.radius = config.MistShapeRadius;
            shape.scale = new Vector3(1f, config.MistShapeHeight / 2f, 1f);

            var vel = _ps.velocityOverLifetime;
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(config.MistOrbitalSpeedMin, config.MistOrbitalSpeedMax);

            var colorLife = _ps.colorOverLifetime;
            var grad = colorLife.color.gradient;
            grad.SetKeys(
                new[] { new GradientColorKey(config.MistStartColor, 0f), new GradientColorKey(config.MistStartColor, 0.3f),
                        new GradientColorKey(config.MistEndColor, 1f) },
                grad.alphaKeys);
            colorLife.color = grad;

            var noise = _ps.noise;
            noise.strength = config.MistNoiseStrength;
            noise.frequency = config.MistNoiseFrequency;

            var renderer = _childGo.GetComponent<ParticleSystemRenderer>();
            _cachedMaterial = renderer.material;
            if (config.ParticleTexture != null)
                _cachedMaterial.mainTexture = config.ParticleTexture;

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
    }
}

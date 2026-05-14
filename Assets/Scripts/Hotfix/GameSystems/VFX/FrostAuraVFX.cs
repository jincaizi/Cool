using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class FrostAuraVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private ParticleSystem _auraParticles;
        [SerializeField] private float _maxEmissionRate = 50f;
        [SerializeField] private float _maxRadius = 1.5f;
        [SerializeField] private float _maxOrbitalSpeed = 3f;

        private bool _isActive;
        private ParticleSystem.EmissionModule _emission;
        private ParticleSystem.MainModule _main;
        private ParticleSystem.VelocityOverLifetimeModule _velocityOverLifetime;

        private void Awake()
        {
            if (_auraParticles == null)
                _auraParticles = GetComponentInChildren<ParticleSystem>();
            if (_auraParticles != null)
            {
                _emission = _auraParticles.emission;
                _main = _auraParticles.main;
                _velocityOverLifetime = _auraParticles.velocityOverLifetime;
                _auraParticles.Stop();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<SkillChargingStartedEvent>(OnChargingStarted);
            EventBus.Subscribe<SkillChargeTickEvent>(OnChargeTick);
            EventBus.Subscribe<SkillReleasedEvent>(OnReleased);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillChargingStartedEvent>(OnChargingStarted);
            EventBus.Unsubscribe<SkillChargeTickEvent>(OnChargeTick);
            EventBus.Unsubscribe<SkillReleasedEvent>(OnReleased);
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0) return true;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnChargingStarted(SkillChargingStartedEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;
            _isActive = true;
            if (_auraParticles != null && !_auraParticles.isPlaying)
                _auraParticles.Play();
            UpdateIntensity(0f);
        }

        private void OnChargeTick(SkillChargeTickEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            UpdateIntensity(e.Progress);
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            _isActive = false;
            if (_auraParticles != null)
                _auraParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void UpdateIntensity(float t)
        {
            if (_auraParticles == null) return;
            _emission.rateOverTime = Mathf.Lerp(5f, _maxEmissionRate, t);
            _main.startColor = Color.Lerp(
                new Color(1f, 1f, 1f, 0.5f),
                new Color(0.2f, 0.5f, 1f, 0.8f), t);
            var shape = _auraParticles.shape;
            shape.radius = Mathf.Lerp(0.8f, _maxRadius, t);
            _velocityOverLifetime.orbitalZ = Mathf.Lerp(1f, _maxOrbitalSpeed, t);
        }
    }
}

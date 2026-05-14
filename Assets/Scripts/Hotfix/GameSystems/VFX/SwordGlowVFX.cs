using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class SwordGlowVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;

        [Header("Glow Settings")]
        [SerializeField] private Color _edgeColor = new Color(0.2f, 0.5f, 1f);
        [SerializeField] private float _glowIntensityMin = 0.15f;
        [SerializeField] private float _glowIntensityMax = 1.5f;

        private WeaponMaterialProxy _materialProxy;
        private bool _isActive;

        private void Awake()
        {
            _materialProxy = GetComponent<WeaponMaterialProxy>();
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
            if (_materialProxy != null)
                _materialProxy.SetGlow(_edgeColor, _glowIntensityMin);
        }

        private void OnChargeTick(SkillChargeTickEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            float intensity = Mathf.Lerp(_glowIntensityMin, _glowIntensityMax, e.Progress);
            if (_materialProxy != null)
                _materialProxy.SetGlow(_edgeColor, intensity);
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            _isActive = false;
            if (_materialProxy != null)
                _materialProxy.SetGlow(Color.black, 0f);
        }
    }
}

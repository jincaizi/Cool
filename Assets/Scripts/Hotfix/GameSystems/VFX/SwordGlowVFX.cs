using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class SwordGlowVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private string _weaponBonePath = "weapon_r";
        [SerializeField] private Color _glowColor = new Color(0.2f, 0.5f, 1f);
        [SerializeField] private float _maxGlowIntensity = 2f;

        private Renderer _weaponRenderer;
        private MaterialPropertyBlock _propBlock;
        private bool _isActive;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            var t = transform.Find(_weaponBonePath);
            if (t != null)
            {
                _weaponRenderer = t.GetComponent<Renderer>();
                if (_weaponRenderer == null)
                    _weaponRenderer = t.GetComponentInChildren<Renderer>();
            }
            if (_weaponRenderer == null)
            {
                var allRenderers = GetComponentsInChildren<Renderer>();
                foreach (var r in allRenderers)
                {
                    if (r.name.ToLower().Contains("weapon") || r.name.ToLower().Contains("sword"))
                    { _weaponRenderer = r; break; }
                }
                if (_weaponRenderer == null && allRenderers.Length > 0)
                    _weaponRenderer = allRenderers[0];
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
            UpdateGlow(0f);
        }

        private void OnChargeTick(SkillChargeTickEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            UpdateGlow(e.Progress);
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            _isActive = false;
            UpdateGlow(0f);
        }

        private void UpdateGlow(float t)
        {
            if (_weaponRenderer == null) return;
            _weaponRenderer.GetPropertyBlock(_propBlock);
            float intensity = Mathf.Lerp(0.3f, _maxGlowIntensity, t);
            _propBlock.SetColor(EmissionColorId, _glowColor * intensity);
            _weaponRenderer.SetPropertyBlock(_propBlock);
        }
    }
}

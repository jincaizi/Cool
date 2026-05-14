using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 剑身发光 — 蓄力时通过 MaterialPropertyBlock 控制 Custom/SwordGlow shader 的辉光属性。
    /// 渐变中心白→边缘冰蓝，脉冲呼吸，符文 UV 流动由 shader 内部处理。
    /// </summary>
    public class SwordGlowVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private string _weaponBonePath = "weapon_r";

        [Header("Glow Settings")]
        [SerializeField] private Color _edgeColor = new Color(0.2f, 0.5f, 1f);
        [SerializeField] private float _glowIntensityMin = 0.15f;
        [SerializeField] private float _glowIntensityMax = 1.5f;

        private Renderer _weaponRenderer;
        private MaterialPropertyBlock _propBlock;
        private bool _isActive;

        private static readonly int EdgeColorId       = Shader.PropertyToID("_EdgeColor");
        private static readonly int GlowIntensityId   = Shader.PropertyToID("_GlowIntensity");

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

            // Ensure glow starts off
            if (_weaponRenderer != null)
            {
                _weaponRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(EdgeColorId, Color.black);
                _propBlock.SetFloat(GlowIntensityId, 0f);
                _weaponRenderer.SetPropertyBlock(_propBlock);
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
            SetGlow(_edgeColor, _glowIntensityMin);
        }

        private void OnChargeTick(SkillChargeTickEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            float intensity = Mathf.Lerp(_glowIntensityMin, _glowIntensityMax, e.Progress);
            SetGlow(_edgeColor, intensity);
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!_isActive || !WatchesSkill(e.SkillId)) return;
            _isActive = false;
            SetGlow(Color.black, 0f);
        }

        private void SetGlow(Color edgeColor, float intensity)
        {
            if (_weaponRenderer == null) return;
            _weaponRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(EdgeColorId, edgeColor);
            _propBlock.SetFloat(GlowIntensityId, intensity);
            _weaponRenderer.SetPropertyBlock(_propBlock);
        }
    }
}

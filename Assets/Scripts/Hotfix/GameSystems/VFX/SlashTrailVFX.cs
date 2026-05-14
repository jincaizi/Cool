using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class SlashTrailVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private TrailRenderer _trailRenderer;
        [SerializeField] private string _weaponBonePath = "weapon_r";
        [SerializeField] private Color _trailColor = new Color(0.2f, 0.5f, 1f);
        [SerializeField] private float _earlyActivateProgress = 0.8f;

        private bool _isActive;
        private bool _earlyActivated;

        private void Awake()
        {
            if (_trailRenderer == null)
            {
                var weaponBone = transform.Find(_weaponBonePath);
                if (weaponBone != null)
                    _trailRenderer = weaponBone.GetComponent<TrailRenderer>();
            }
            if (_trailRenderer != null)
            {
                _trailRenderer.emitting = false;
                _trailRenderer.time = 0.1f;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(_trailColor, 0f), new GradientColorKey(new Color(_trailColor.r, _trailColor.g, _trailColor.b, 0f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                _trailRenderer.colorGradient = gradient;
                _trailRenderer.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 0f));
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<SkillChargeTickEvent>(OnChargeTick);
            EventBus.Subscribe<SkillReleasedEvent>(OnReleased);
        }

        private void OnDisable()
        {
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

        private void OnChargeTick(SkillChargeTickEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;
            if (!_earlyActivated && e.Progress >= _earlyActivateProgress)
            {
                _earlyActivated = true;
                _isActive = true;
                if (_trailRenderer != null) _trailRenderer.emitting = true;
            }
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;
            if (!_earlyActivated)
            {
                _isActive = true;
                if (_trailRenderer != null) _trailRenderer.emitting = true;
            }
            if (_trailRenderer != null)
                Invoke(nameof(StopTrail), _trailRenderer.time + 0.05f);
        }

        private void StopTrail()
        {
            _isActive = false;
            _earlyActivated = false;
            if (_trailRenderer != null) _trailRenderer.emitting = false;
        }
    }
}

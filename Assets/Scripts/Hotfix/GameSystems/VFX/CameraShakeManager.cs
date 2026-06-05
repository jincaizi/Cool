using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class CameraShakeManager : MonoBehaviour
    {
        public static bool EnableVFX = false;

        [SerializeField] private HitFeedbackProfile _profile;
        [SerializeField] private Transform _camera;

        private Vector3 _originalLocalPos;
        private float _shakeEndTime;
        private float _currentIntensity;

        private void Start()
        {
            if (_camera == null) _camera = Camera.main.transform;
            _originalLocalPos = _camera.localPosition;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnHit);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnHit);
            if (_camera != null)
                _camera.localPosition = _originalLocalPos;
            _currentIntensity = 0f;
        }

        private void OnHit(MonsterTakeDamageEvent e)
        {
            if (!EnableVFX) return;
            float intensity = e.SkillId > 0
                ? _profile.SkillShakeIntensity
                : _profile.NormalShakeIntensity;
            if (e.IsCritical) intensity *= _profile.CritShakeMultiplier;

            _currentIntensity = Mathf.Max(_currentIntensity, intensity);
            _shakeEndTime = Time.unscaledTime + _profile.ShakeDuration;
        }

        private void LateUpdate()
        {
            if (_camera == null) return;

            if (Time.unscaledTime >= _shakeEndTime)
            {
                if (_currentIntensity > 0f)
                {
                    _camera.localPosition = _originalLocalPos;
                    _currentIntensity = 0f;
                }
                return;
            }

            float t = (_shakeEndTime - Time.unscaledTime) / _profile.ShakeDuration;
            float shake = _currentIntensity * t;

            float x = (Mathf.PerlinNoise(Time.unscaledTime * 25f, 0f) - 0.5f) * 2f * shake * 0.05f;
            float y = (Mathf.PerlinNoise(0f, Time.unscaledTime * 25f) - 0.5f) * 2f * shake * 0.05f;

            _camera.localPosition = _originalLocalPos + new Vector3(x, y, 0f);
        }
    }
}

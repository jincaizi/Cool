using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponVFXController : MonoBehaviour
    {
        [SerializeField] private WeaponElementConfig _elementConfig;
        [SerializeField] private float _swingThreshold = 120f;
        [SerializeField] private float _swingCooldown = 0.3f;

        private WeaponMaterialProxy _materialProxy;
        private WeaponMistParticles _mistParticles;
        private WeaponSurfaceShader _surfaceShader;
        private WeaponTrailRenderer _trailRenderer;

        private bool _isActive;
        private bool _isSwinging;
        private float _swingTimer;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;

        private void Awake()
        {
            _materialProxy = GetComponent<WeaponMaterialProxy>();
            _mistParticles = GetComponent<WeaponMistParticles>();
            _surfaceShader = GetComponent<WeaponSurfaceShader>();
            _trailRenderer = GetComponent<WeaponTrailRenderer>();

            if (_elementConfig == null)
            {
                Debug.LogWarning($"[WeaponVFXController] {name} missing WeaponElementConfig, effect disabled");
                enabled = false;
                return;
            }

            var renderer = _materialProxy?.WeaponRenderer;
            if (renderer != null && !renderer.sharedMaterial.shader.name.Contains("SwordGlow"))
            {
                Debug.LogWarning(
                    $"[WeaponVFXController] {name} weapon material uses '{renderer.sharedMaterial.shader.name}', " +
                    $"expected 'Custom/SwordGlow'. Frost shader overlay won't work. " +
                    $"Assign a material using the 'Custom/SwordGlow' shader.");
            }
        }

        private void Start()
        {
            if (_materialProxy == null)
            {
                Debug.LogWarning($"[WeaponVFXController] {name} missing WeaponMaterialProxy component");
                enabled = false;
                return;
            }
            _mistParticles?.Init(_elementConfig);
            _trailRenderer?.Init(_elementConfig);
            _surfaceShader?.Init(_materialProxy, _elementConfig);

            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
        }

        private void Update()
        {
            if (!_isActive || _elementConfig == null) return;

            float angularSpeed = Quaternion.Angle(_lastRotation, transform.rotation) / Time.deltaTime;

            if (!_isSwinging && angularSpeed > _swingThreshold)
            {
                _isSwinging = true;
                OnEnterSwinging();
            }
            else if (_isSwinging && angularSpeed < _swingThreshold)
            {
                _swingTimer += Time.deltaTime;
                if (_swingTimer >= _swingCooldown)
                {
                    _isSwinging = false;
                    _swingTimer = 0f;
                    OnEnterIdle();
                }
            }
            else if (_isSwinging && angularSpeed >= _swingThreshold)
            {
                _swingTimer = 0f;
            }

            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
        }

        private void OnEnterIdle()
        {
            _mistParticles?.SetVisible(true);
            _surfaceShader?.SetFrostActive(true);
            _trailRenderer?.SetEmitting(false);
        }

        private void OnEnterSwinging()
        {
            _mistParticles?.SetVisible(false);
            _surfaceShader?.SetFrostActive(false);
            _trailRenderer?.SetEmitting(true);
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (!active)
            {
                _mistParticles?.SetVisible(false);
                _surfaceShader?.SetFrostActive(false);
                _trailRenderer?.SetEmitting(false);
                _isSwinging = false;
                _swingTimer = 0f;
            }
            else
            {
                _lastPosition = transform.position;
                _lastRotation = transform.rotation;
                OnEnterIdle();
            }
        }
    }
}

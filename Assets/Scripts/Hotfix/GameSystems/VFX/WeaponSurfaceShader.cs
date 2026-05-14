using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponSurfaceShader : MonoBehaviour
    {
        private WeaponMaterialProxy _proxy;
        private WeaponElementConfig _config;
        private Coroutine _blendCoroutine;
        private float _currentAmount;

        public void Init(WeaponMaterialProxy proxy, WeaponElementConfig config)
        {
            _proxy = proxy;
            _config = config;
        }

        public void SetFrostActive(bool active)
        {
            if (_proxy == null || _config == null) return;
            if (_blendCoroutine != null)
                StopCoroutine(_blendCoroutine);
            _blendCoroutine = StartCoroutine(BlendRoutine(active ? _config.FrostAmount : 0f));
        }

        private System.Collections.IEnumerator BlendRoutine(float target)
        {
            float start = _currentAmount;
            float elapsed = 0f;
            float duration = _config.FrostBlendTime;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _currentAmount = Mathf.Lerp(start, target, elapsed / duration);
                _proxy.SetFrost(_config.FrostColor, _currentAmount, _config.FrostFlowSpeed);
                yield return null;
            }
            _currentAmount = target;
            _proxy.SetFrost(_config.FrostColor, _currentAmount, _config.FrostFlowSpeed);
            _blendCoroutine = null;
        }
    }
}

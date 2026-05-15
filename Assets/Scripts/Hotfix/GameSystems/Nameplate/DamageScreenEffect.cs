using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.Nameplate
{
    public class DamageScreenEffect
    {
        private readonly Image _overlay;
        private float _lastFlashTime = -999f;
        private const float FlashCooldown = 3f;

        public DamageScreenEffect(Transform canvasTransform)
        {
            var go = new GameObject("DamageOverlay");
            go.transform.SetParent(canvasTransform, false);
            _overlay = go.AddComponent<Image>();
            _overlay.color = new Color(1f, 0f, 0f, 0f);
            _overlay.raycastTarget = false;

            var rt = _overlay.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public void Flash()
        {
            if (Time.time - _lastFlashTime < FlashCooldown) return;
            _lastFlashTime = Time.time;

            _overlay.DOKill();
            _overlay.DOFade(0.15f, 0.1f).OnComplete(() =>
            {
                _overlay.DOFade(0f, 2.5f);
            });
        }

        public void Cleanup()
        {
            _overlay.DOKill();
            if (_overlay != null && _overlay.gameObject != null)
                Object.Destroy(_overlay.gameObject);
        }
    }
}

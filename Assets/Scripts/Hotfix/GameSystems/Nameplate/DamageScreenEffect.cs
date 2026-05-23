using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using Core.Resource;
using DataDefinition;

namespace Hotfix.GameSystems.Nameplate
{
    public class DamageScreenEffect
    {
        private readonly Image _overlay;
        private readonly float _cooldown;
        private float _lastFlashTime = 0f;

        private const int FlashCount = 3;
        private const float FlashOnDuration = 0.08f;
        private const float FlashOffDuration = 0.05f;
        private const float FlashFadeDuration = 1.5f;
        private const float FlashAlpha = 0.15f;

        public DamageScreenEffect(Transform canvasTransform)
        {
            var go = new GameObject("DamageOverlay");
            go.transform.SetParent(canvasTransform, false);
            _overlay = go.AddComponent<Image>();
            _overlay.raycastTarget = false;

            var rt = _overlay.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _overlay.sprite = GameSettings.Instance.HitFlashSprite;
            var initColor = GameSettings.Instance.HitFlashColor;
            _overlay.color = new Color(initColor.r, initColor.g, initColor.b, 0f);
            _cooldown = GameSettings.Instance.HitFlashCD;
        }

        public void Flash()
        {
            if (_cooldown > 0f && Time.time - _lastFlashTime < _cooldown) return;
            _lastFlashTime = Time.time;

            _overlay.DOKill();

            var seq = DOTween.Sequence();
            for (int i = 0; i < FlashCount; i++)
            {
                seq.Append(_overlay.DOFade(FlashAlpha, FlashOnDuration));
                seq.AppendInterval(FlashOffDuration);
                seq.Append(_overlay.DOFade(0f, FlashOffDuration));
                if (i < FlashCount - 1)
                    seq.AppendInterval(FlashOffDuration);
            }
            seq.Append(_overlay.DOFade(FlashAlpha, FlashOffDuration));
            seq.Append(_overlay.DOFade(0f, FlashFadeDuration));
        }

        public void Cleanup()
        {
            _overlay.DOKill();
            if (_overlay != null && _overlay.gameObject != null)
                Object.Destroy(_overlay.gameObject);
        }
    }
}

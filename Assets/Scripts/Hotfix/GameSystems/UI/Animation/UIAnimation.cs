using DG.Tweening;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public class UIAnimation : MonoBehaviour
    {
        public UIAnimPreset ShowPreset;
        public UIAnimPreset HidePreset;

        private CanvasGroup _canvasGroup;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        public Sequence PlayShow()
        {
            KillAll();
            var seq = BuildSequence(ShowPreset, isShow: true);
            seq.Play();
            return seq;
        }

        public Sequence PlayHide()
        {
            KillAll();
            var seq = BuildSequence(HidePreset, isShow: false);
            seq.Play();
            return seq;
        }

        public SequenceBuilder Build()
        {
            KillAll();
            return new SequenceBuilder(transform, _canvasGroup);
        }

        public void KillAll()
        {
            DOTween.Kill(transform);
        }

        public Tweener FadeIn(float duration = 0.3f)
        {
            if (_canvasGroup == null) return null;
            _canvasGroup.alpha = 0f;
            return _canvasGroup.DOFade(1f, duration).SetTarget(transform);
        }

        public Tweener ScaleIn(float duration = 0.3f)
        {
            transform.localScale = Vector3.one * 0.9f;
            return transform.DOScale(1f, duration).SetTarget(transform);
        }

        public Tweener SlideIn(Direction dir, float duration = 0.3f)
        {
            var rt = transform as RectTransform;
            if (rt == null) return null;

            var distance = dir switch
            {
                Direction.Left or Direction.Right => rt.rect.width,
                Direction.Top or Direction.Bottom => rt.rect.height,
                _ => 100f
            };

            var offset = dir switch
            {
                Direction.Left   => new Vector2(-distance, 0),
                Direction.Right  => new Vector2(distance, 0),
                Direction.Top    => new Vector2(0, distance),
                Direction.Bottom => new Vector2(0, -distance),
                _ => Vector2.zero
            };

            var startPos = rt.anchoredPosition;
            rt.anchoredPosition = startPos + offset;
            return rt.DOAnchorPos(startPos, duration).SetTarget(transform);
        }

        private Sequence BuildSequence(UIAnimPreset preset, bool isShow)
        {
            if (preset == null) return DOTween.Sequence();

            var seq = DOTween.Sequence();

            if (preset.Delay > 0f)
                seq.AppendInterval(preset.Delay);

            if (isShow)
            {
                if (preset.Fade && _canvasGroup != null)
                {
                    _canvasGroup.alpha = 0f;
                    seq.Join(_canvasGroup.DOFade(1f, preset.Duration).SetEase(preset.Ease));
                }
                if (preset.Scale)
                {
                    transform.localScale = Vector3.one * 0.9f;
                    seq.Join(transform.DOScale(1f, preset.Duration).SetEase(preset.Ease));
                }
                if (preset.Slide)
                {
                    var rt = transform as RectTransform;
                    if (rt != null)
                    {
                        var dist = preset.SlideDir is Direction.Left or Direction.Right ? rt.rect.width : rt.rect.height;
                        var off = preset.SlideDir switch
                        {
                            Direction.Left   => new Vector2(-dist, 0),
                            Direction.Right  => new Vector2(dist, 0),
                            Direction.Top    => new Vector2(0, dist),
                            Direction.Bottom => new Vector2(0, -dist),
                            _ => Vector2.zero
                        };
                        var orig = rt.anchoredPosition;
                        rt.anchoredPosition = orig + off;
                        seq.Join(rt.DOAnchorPos(orig, preset.Duration).SetEase(preset.Ease));
                    }
                }
            }
            else
            {
                if (preset.Fade && _canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                    seq.Join(_canvasGroup.DOFade(0f, preset.Duration).SetEase(preset.Ease));
                }
                if (preset.Scale)
                {
                    transform.localScale = Vector3.one;
                    seq.Join(transform.DOScale(0.95f, preset.Duration).SetEase(preset.Ease));
                }
                if (preset.Slide)
                {
                    var rt = transform as RectTransform;
                    if (rt != null)
                    {
                        var dist = preset.SlideDir is Direction.Left or Direction.Right ? rt.rect.width : rt.rect.height;
                        var off = preset.SlideDir switch
                        {
                            Direction.Left   => new Vector2(-dist, 0),
                            Direction.Right  => new Vector2(dist, 0),
                            Direction.Top    => new Vector2(0, dist),
                            Direction.Bottom => new Vector2(0, -dist),
                            _ => Vector2.zero
                        };
                        rt.DOAnchorPos(rt.anchoredPosition + off, preset.Duration).SetEase(preset.Ease);
                    }
                }
            }

            seq.SetTarget(transform);
            return seq;
        }
    }
}

using System;
using DG.Tweening;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public class SequenceBuilder
    {
        private readonly Transform _target;
        private readonly CanvasGroup _canvasGroup;
        private readonly Sequence _sequence;

        public SequenceBuilder(Transform target, CanvasGroup canvasGroup)
        {
            _target = target;
            _canvasGroup = canvasGroup;
            _sequence = DOTween.Sequence();
        }

        public SequenceBuilder FadeIn(float duration = 0.3f)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _sequence.Join(_canvasGroup.DOFade(1f, duration));
            }
            return this;
        }

        public SequenceBuilder FadeOut(float duration = 0.3f)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _sequence.Join(_canvasGroup.DOFade(0f, duration));
            }
            return this;
        }

        public SequenceBuilder ScaleIn(float duration = 0.3f, float fromScale = 0.9f)
        {
            _target.localScale = Vector3.one * fromScale;
            _sequence.Join(_target.DOScale(1f, duration));
            return this;
        }

        public SequenceBuilder ScaleOut(float duration = 0.3f, float toScale = 0.95f)
        {
            _target.localScale = Vector3.one;
            _sequence.Join(_target.DOScale(toScale, duration));
            return this;
        }

        public SequenceBuilder SlideIn(Direction dir, float duration = 0.3f, float distance = 100f)
        {
            var anchored = _target as RectTransform;
            if (anchored == null) return this;

            var startPos = anchored.anchoredPosition;
            var offset = dir switch
            {
                Direction.Left   => new Vector2(-distance, 0),
                Direction.Right  => new Vector2(distance, 0),
                Direction.Top    => new Vector2(0, distance),
                Direction.Bottom => new Vector2(0, -distance),
                _ => Vector2.zero
            };

            anchored.anchoredPosition = startPos + offset;
            _sequence.Join(anchored.DOAnchorPos(startPos, duration));
            return this;
        }

        public SequenceBuilder Join(Action action)
        {
            action();
            return this;
        }

        public SequenceBuilder Then(Action action)
        {
            _sequence.AppendCallback(() => action());
            return this;
        }

        public SequenceBuilder Delay(float seconds)
        {
            _sequence.AppendInterval(seconds);
            return this;
        }

        public Sequence Play()
        {
            return _sequence.Play();
        }
    }
}

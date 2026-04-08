using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Framework.Animation
{
    /// <summary>
    /// DOTween animation extensions for UI panels.
    /// Provides convenient tween methods for common animations.
    /// </summary>
    public static class UIAnimation
    {
        // Default easing
        private const Ease DefaultEase = Ease.OutQuad;
        private const Ease DefaultCloseEase = Ease.InQuad;

        #region Scale Animations

        /// <summary>
        /// Scale in animation (from zero to original).
        /// </summary>
        public static Tweener ScaleIn(this RectTransform rect, float duration = 0.3f, Action onComplete = null, Vector3? fromScale = null)
        {
            rect.localScale = fromScale ?? Vector3.zero;
            return rect.DOScale(1f, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Scale out animation (from original to zero).
        /// </summary>
        public static Tweener ScaleOut(this RectTransform rect, float duration = 0.3f, Action onComplete = null, Vector3? toScale = null)
        {
            return rect.DOScale(toScale ?? Vector3.zero, duration)
                .SetEase(DefaultCloseEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Scale in from a specific anchor position.
        /// </summary>
        public static Tweener ScaleInFrom(this RectTransform rect, Vector3 startScale, float duration = 0.3f, Action onComplete = null)
        {
            rect.localScale = startScale;
            return rect.DOScale(1f, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        #endregion

        #region Fade Animations

        /// <summary>
        /// Fade in animation using CanvasGroup.
        /// </summary>
        public static Tweener FadeIn(this CanvasGroup canvasGroup, float duration = 0.3f, Action onComplete = null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
            return canvasGroup.DOFade(1f, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Fade out animation using CanvasGroup.
        /// </summary>
        public static Tweener FadeOut(this CanvasGroup canvasGroup, float duration = 0.3f, Action onComplete = null, bool disableRaycasts = true)
        {
            return canvasGroup.DOFade(0f, duration)
                .SetEase(DefaultCloseEase)
                .OnComplete(() =>
                {
                    if (disableRaycasts)
                        canvasGroup.blocksRaycasts = false;
                    onComplete?.Invoke();
                });
        }

        /// <summary>
        /// Fade in using Image.color.
        /// </summary>
        public static Tweener FadeIn(this Image image, float duration = 0.3f, Action onComplete = null)
        {
            var color = image.color;
            color.a = 0f;
            image.color = color;
            return image.DOFade(1f, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Fade out using Image.color.
        /// </summary>
        public static Tweener FadeOut(this Image image, float duration = 0.3f, Action onComplete = null)
        {
            var color = image.color;
            color.a = 0f;
            return image.DOColor(color, duration)
                .SetEase(DefaultCloseEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        #endregion

        #region Slide Animations

        /// <summary>
        /// Slide in from a direction.
        /// </summary>
        public static Tweener SlideIn(this RectTransform rect, Vector2 startPos, float duration = 0.3f, Action onComplete = null)
        {
            var originalPos = rect.anchoredPosition;
            rect.anchoredPosition = startPos;

            return rect.DOAnchorPos(originalPos, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Slide out to a direction.
        /// </summary>
        public static Tweener SlideOut(this RectTransform rect, Vector2 endPos, float duration = 0.3f, Action onComplete = null)
        {
            return rect.DOAnchorPos(endPos, duration)
                .SetEase(DefaultCloseEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Slide in from left.
        /// </summary>
        public static Tweener SlideInFromLeft(this RectTransform rect, float offset = -100f, float duration = 0.3f, Action onComplete = null)
        {
            return rect.SlideIn(new Vector2(rect.anchoredPosition.x + offset, rect.anchoredPosition.y), duration, onComplete);
        }

        /// <summary>
        /// Slide out to left.
        /// </summary>
        public static Tweener SlideOutToLeft(this RectTransform rect, float offset = -100f, float duration = 0.3f, Action onComplete = null)
        {
            return rect.SlideOut(new Vector2(rect.anchoredPosition.x + offset, rect.anchoredPosition.y), duration, onComplete);
        }

        #endregion

        #region Pop Animations

        /// <summary>
        /// Pop in animation (scale overshoot).
        /// </summary>
        public static Tweener PopIn(this RectTransform rect, float duration = 0.4f, Action onComplete = null)
        {
            rect.localScale = Vector3.zero;
            return rect.DOScale(1f, duration)
                .SetEase(Ease.OutBack)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Pop out animation (scale shrink).
        /// </summary>
        public static Tweener PopOut(this RectTransform rect, float duration = 0.3f, Action onComplete = null)
        {
            return rect.DOScale(0f, duration)
                .SetEase(Ease.InBack)
                .OnComplete(() => onComplete?.Invoke());
        }

        #endregion

        #region Utility

        /// <summary>
        /// Kill all tweens on a RectTransform.
        /// </summary>
        public static void KillAllTweens(this RectTransform rect)
        {
            rect.DOKill();
        }

        /// <summary>
        /// Kill all tweens on a CanvasGroup.
        /// </summary>
        public static void KillAllTweens(this CanvasGroup canvasGroup)
        {
            canvasGroup.DOKill();
        }

        #endregion
    }
}

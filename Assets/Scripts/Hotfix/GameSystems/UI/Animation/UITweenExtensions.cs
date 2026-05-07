using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public static class UITweenExtensions
    {
        /// <summary>Punch scale — icon click bounce / hit feedback</summary>
        public static Tweener Punch(this Transform target, float strength = 0.2f, float duration = 0.15f)
        {
            return target.DOPunchScale(Vector3.one * strength, duration, 1, 0f);
        }

        /// <summary>Shake position — error / invalid input feedback</summary>
        public static Tweener Shake(this Transform target, float strength = 10f, float duration = 0.3f)
        {
            return target.DOShakeAnchorPos(duration, strength, 20, 90f, false, true);
        }

        /// <summary>Count up a numeric Text from 0 to target value</summary>
        public static Tweener CountUp(this Text text, int targetValue, float duration = 0.5f)
        {
            var current = 0;
            return DOTween.To(() => current, v =>
            {
                current = v;
                text.text = current.ToString();
            }, targetValue, duration);
        }

        /// <summary>Flash graphic color — highlight / on-hit white flash</summary>
        public static Tweener Flash(this Graphic graphic, Color flashColor, float duration = 0.1f)
        {
            var original = graphic.color;
            graphic.color = flashColor;
            return graphic.DOColor(original, duration);
        }

        /// <summary>Flash white (convenience overload)</summary>
        public static Tweener FlashWhite(this Graphic graphic, float duration = 0.1f)
        {
            return graphic.Flash(Color.white, duration);
        }
    }
}

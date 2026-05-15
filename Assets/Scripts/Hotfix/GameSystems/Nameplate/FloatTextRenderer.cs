using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class FloatTextRenderer
    {
        private readonly Transform _canvasTransform;
        private readonly Stack<TextMeshProUGUI> _pool = new();
        private readonly HashSet<TextMeshProUGUI> _active = new();
        private readonly Dictionary<long, MergeEntry> _mergeTracker = new();
        private const float MergeWindow = 0.2f;

        public Camera Camera { get; set; }

        private class MergeEntry
        {
            public int Count;
            public int Sum;
            public float LastHitTime;
            public TextMeshProUGUI Tmp;
        }

        public FloatTextRenderer(Transform canvasTransform)
        {
            _canvasTransform = canvasTransform;
        }

        public void ShowFloatingText(Vector3 worldPos, FloatTextSettings settings, string text)
        {
            Spawn(worldPos, settings, 0, text);
        }

        public void ShowDamageText(int entityId, Vector3 worldPos, FloatTextSettings settings, int value)
        {
            var mergeKey = MakeMergeKey(entityId, settings.Type);

            if (_mergeTracker.TryGetValue(mergeKey, out var merge)
                && Time.time - merge.LastHitTime < MergeWindow)
            {
                merge.Count++;
                merge.Sum += value;
                merge.LastHitTime = Time.time;
                merge.Tmp.text = $"-{merge.Sum}";
                merge.Tmp.alpha = 1f;
                return;
            }

            var tmp = Spawn(worldPos, settings, value, null);

            if (settings.Type == FloatTextType.Normal || settings.Type == FloatTextType.Crit)
            {
                _mergeTracker[mergeKey] = new MergeEntry
                {
                    Count = 1,
                    Sum = value,
                    LastHitTime = Time.time,
                    Tmp = tmp
                };
            }
        }

        public void PurgeExpiredMerges()
        {
            var expired = new List<long>();
            foreach (var kv in _mergeTracker)
                if (Time.time - kv.Value.LastHitTime > MergeWindow)
                    expired.Add(kv.Key);
            foreach (var k in expired)
                _mergeTracker.Remove(k);
        }

        public void Cleanup()
        {
            foreach (var tmp in _active)
                if (tmp != null) Object.Destroy(tmp.gameObject);
            _active.Clear();

            while (_pool.Count > 0)
            {
                var tmp = _pool.Pop();
                if (tmp != null) Object.Destroy(tmp.gameObject);
            }
        }

        private TextMeshProUGUI Spawn(Vector3 worldPos, FloatTextSettings settings, int value, string textOverride)
        {
            var tmp = Rent();
            _active.Add(tmp);

            if (!string.IsNullOrEmpty(textOverride))
                tmp.text = textOverride;
            else
                tmp.text = settings.Type == FloatTextType.Heal ? $"+{value}" : $"-{value}";

            tmp.color = settings.Color;
            tmp.fontSize = settings.FontSize;
            if (settings.Font != null) tmp.font = settings.Font;
            if (settings.FontMaterial != null) tmp.fontMaterial = settings.FontMaterial;
            tmp.alpha = 1f;

            if (Camera != null)
            {
                var screenPos = Camera.WorldToScreenPoint(worldPos);
                tmp.rectTransform.position = screenPos;
            }

            var rt = tmp.rectTransform;
            var startY = rt.anchoredPosition.y;
            var seq = DOTween.Sequence();

            switch (settings.Type)
            {
                case FloatTextType.Crit:
                    rt.localScale = Vector3.one * 0.6f;
                    seq.Append(rt.DOScale(1.3f, 0.15f).SetEase(Ease.OutBack));
                    seq.Join(rt.DOAnchorPosY(startY + settings.MoveUpDistance, settings.Duration)
                        .SetEase(Ease.OutCubic));
                    seq.Join(tmp.DOFade(0f, settings.Duration * (1f - settings.FadeStartRatio))
                        .SetDelay(settings.Duration * settings.FadeStartRatio));
                    break;

                case FloatTextType.Dodge:
                case FloatTextType.Block:
                    seq.Join(rt.DOAnchorPos(new Vector2(
                        rt.anchoredPosition.x + Random.Range(20f, 40f),
                        startY + settings.MoveUpDistance), settings.Duration)
                        .SetEase(Ease.OutCubic));
                    seq.Join(tmp.DOFade(0f, settings.Duration * (1f - settings.FadeStartRatio))
                        .SetDelay(settings.Duration * settings.FadeStartRatio));
                    break;

                case FloatTextType.SkillName:
                    rt.localScale = Vector3.one * 0.5f;
                    seq.Append(rt.DOScale(1.2f, 0.2f).SetEase(Ease.OutBack));
                    seq.Append(rt.DOScale(0.8f, settings.Duration - 0.2f));
                    seq.Join(tmp.DOFade(0f, settings.Duration * (1f - settings.FadeStartRatio))
                        .SetDelay(settings.Duration * settings.FadeStartRatio));
                    break;

                default: // Normal, Heal, DOT
                    rt.localScale = Vector3.one * settings.StartScale;
                    seq.Join(rt.DOAnchorPosY(startY + settings.MoveUpDistance, settings.Duration)
                        .SetEase(Ease.OutCubic));
                    seq.Join(tmp.DOFade(0f, settings.Duration * (1f - settings.FadeStartRatio))
                        .SetDelay(settings.Duration * settings.FadeStartRatio));
                    break;
            }

            seq.OnKill(() =>
            {
                _active.Remove(tmp);
                tmp.text = "";
                tmp.alpha = 1f;
                tmp.rectTransform.localScale = Vector3.one;
                tmp.gameObject.SetActive(false);
                _pool.Push(tmp);
            });
            seq.SetTarget(tmp.transform);

            return tmp;
        }

        private TextMeshProUGUI Rent()
        {
            if (_pool.Count > 0)
            {
                var tmp = _pool.Pop();
                tmp.gameObject.SetActive(true);
                return tmp;
            }
            return CreateTMP(active: true);
        }

        private TextMeshProUGUI CreateTMP(bool active)
        {
            var go = new GameObject("FloatText");
            go.transform.SetParent(_canvasTransform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.raycastTarget = false;
            tmp.alignment = TextAlignmentOptions.Center;
            go.SetActive(active);
            return tmp;
        }

        private static long MakeMergeKey(int entityId, FloatTextType type)
        {
            return ((long)entityId << 32) | (long)type;
        }
    }
}

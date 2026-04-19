using System;
using System.Collections.Generic;
using DG.Tweening;
using Hotfix.GameSystems.UI.Framework.Animation;
using UnityEngine;
using UnityEngine.UI;
using Hotfix.GameSystems.UI.Framework.Core;

namespace Hotfix.GameSystems.UI.Components
{
    /// <summary>
    /// Floating tips component.
    /// Shows contextual tips near UI elements.
    /// </summary>
    public class Tips : MonoBehaviour
    {
        [SerializeField] private RectTransform _rect;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _contentText;
        [SerializeField] private Image _background;

        private static Tips _instance;
        private Queue<TipsItem> _pendingTips = new();
        private bool _isShowing;

        private struct TipsItem
        {
            public string Content;
            public Vector2 Position;
            public float Duration;
        }

        public static Tips Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateInstance();
                }
                return _instance;
            }
        }

        private static Tips CreateInstance()
        {
            var go = new GameObject("Tips");
            var tips = go.AddComponent<Tips>();
            tips.CreateLayout();

            tips.gameObject.SetActive(false);
            DontDestroyOnLoad(go);
            return tips;
        }

        private void CreateLayout()
        {
            _rect = gameObject.AddComponent<RectTransform>();
            _rect.sizeDelta = new Vector2(200, 60);

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            _background = CreateImage("Background");
            _background.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            _contentText = CreateText("Content");
            _contentText.fontSize = 20;
            _contentText.color = Color.white;
            _contentText.supportRichText = true;
        }

        private Image CreateImage(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.StretchParent();
            return img;
        }

        private Text CreateText(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.StretchParent();
            rt.sizeDelta = new Vector2(-20, 0);
            return text;
        }

        /// <summary>
        /// Show tips at screen position.
        /// </summary>
        public static void ShowAt(string content, Vector2 screenPos, float duration = 3f)
        {
            Instance.ShowTips(content, screenPos, duration);
        }

        /// <summary>
        /// Show tips anchored to a RectTransform.
        /// </summary>
        public static void ShowAnchored(string content, RectTransform anchor, float duration = 3f)
        {
            if (anchor == null) return;

            Vector2 pos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                anchor.root as RectTransform,
                RectTransformUtility.WorldToScreenPoint(Camera.main, anchor.position),
                null,
                out pos);

            Instance.ShowTips(content, pos, duration);
        }

        /// <summary>
        /// Hide current tips.
        /// </summary>
        public static void Hide()
        {
            Instance.HideTips();
        }

        private void ShowTips(string content, Vector2 position, float duration)
        {
            _pendingTips.Enqueue(new TipsItem
            {
                Content = content,
                Position = position,
                Duration = duration
            });

            ProcessQueue();
        }

        private void ProcessQueue()
        {
            if (_isShowing || _pendingTips.Count == 0)
                return;

            var item = _pendingTips.Dequeue();
            ShowTipsImmediate(item);
        }

        private void ShowTipsImmediate(TipsItem item)
        {
            _isShowing = true;
            _canvasGroup?.DOKill();
            gameObject.SetActive(true);

            _rect.anchoredPosition = item.Position;
            _contentText.text = item.Content;

            // Auto-size based on content
            var preferredWidth = Mathf.Min(_contentText.preferredWidth + 40, 400);
            _rect.sizeDelta = new Vector2(preferredWidth, 60);

            _canvasGroup.DOFade(1f, 0.2f).OnComplete(() =>
            {
                DOVirtual.DelayedCall(item.Duration, () =>
                {
                    _canvasGroup.DOFade(0f, 0.2f).OnComplete(() =>
                    {
                        gameObject.SetActive(false);
                        _isShowing = false;
                        ProcessQueue();
                    });
                });
            });
        }

        private void HideTips()
        {
            _pendingTips.Clear();
            _canvasGroup?.DOKill();
            _canvasGroup.DOFade(0f, 0.1f).OnComplete(() =>
            {
                gameObject.SetActive(false);
                _isShowing = false;
            });
        }
    }
}

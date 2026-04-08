using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using Hotfix.GameSystems.UI.Framework.Animation;

namespace Hotfix.GameSystems.UI.Components
{
    /// <summary>
    /// Toast notification component.
    /// Shows brief messages that auto-dismiss.
    /// </summary>
    public class Toast : MonoBehaviour
    {
        [SerializeField] private Text _messageText;
        [SerializeField] private Image _iconImage;
        [SerializeField] private RectTransform _rect;
        [SerializeField] private CanvasGroup _canvasGroup;

        private static Toast _instance;
        private static Queue<ToastItem> _pendingToasts = new();
        private bool _isShowing;

        private class ToastItem
        {
            public string Message;
            public Sprite Icon;
            public float Duration;
        }

        public static Toast Instance
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

        private static Toast CreateInstance()
        {
            // Create programmatic toast
            var go = new GameObject("Toast");
            _instance = go.AddComponent<Toast>();
            _instance.CreateLayout();

            DontDestroyOnLoad(_instance.gameObject);
            return _instance;
        }

        private void CreateLayout()
        {
            _rect = gameObject.AddComponent<RectTransform>();
            _rect.anchorMin = new Vector2(0.5f, 0f);
            _rect.anchorMax = new Vector2(0.5f, 0f);
            _rect.pivot = new Vector2(0.5f, 0f);
            _rect.anchoredPosition = new Vector2(0, 100f);
            _rect.sizeDelta = new Vector2(400, 60);

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            var bg = new GameObject("Background");
            bg.transform.SetParent(transform);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0, 0, 0, 0.8f);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.StretchParent();

            _messageText = new GameObject("Message").AddComponent<Text>();
            _messageText.transform.SetParent(transform);
            _messageText.text = "";
            _messageText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _messageText.fontSize = 24;
            _messageText.color = Color.white;
            _messageText.alignment = TextAnchor.MiddleCenter;
            _messageText.raycastTarget = false;
            var textRect = _messageText.GetComponent<RectTransform>();
            textRect.StretchParent();
            textRect.sizeDelta = new Vector2(-20, 0);

            _iconImage = new GameObject("Icon").AddComponent<Image>();
            _iconImage.transform.SetParent(transform);
            _iconImage.raycastTarget = false;
            var iconRect = _iconImage.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.5f);
            iconRect.anchorMax = new Vector2(0, 0.5f);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = new Vector2(30, 0);
            iconRect.sizeDelta = new Vector2(40, 40);

            gameObject.SetActive(false);
        }

        public static void Show(string message, float duration = 2f)
        {
            Show(message, null, duration);
        }

        public static void Show(string message, Sprite icon, float duration = 2f)
        {
            _pendingToasts.Enqueue(new ToastItem
            {
                Message = message,
                Icon = icon,
                Duration = duration
            });

            Instance.ProcessQueue();
        }

        private void ProcessQueue()
        {
            if (_isShowing || _pendingToasts.Count == 0)
                return;

            var item = _pendingToasts.Dequeue();
            ShowToast(item);
        }

        private void ShowToast(ToastItem item)
        {
            _isShowing = true;
            gameObject.SetActive(true);

            _messageText.text = item.Message;

            if (item.Icon != null)
            {
                _iconImage.sprite = item.Icon;
                _iconImage.gameObject.SetActive(true);
                _messageText.alignment = TextAnchor.MiddleLeft;
                var textRect = _messageText.GetComponent<RectTransform>();
                textRect.anchoredPosition = new Vector2(60, 0);
            }
            else
            {
                _iconImage.gameObject.SetActive(false);
                _messageText.alignment = TextAnchor.MiddleCenter;
            }

            _rect.ScaleOut(0f, () => { });
            _rect.ScaleIn(0.3f, () =>
            {
                DOVirtual.DelayedCall(item.Duration, () =>
                {
                    _rect.ScaleOut(0.2f, () =>
                    {
                        gameObject.SetActive(false);
                        _isShowing = false;
                        ProcessQueue();
                    });
                });
            });
        }
    }
}

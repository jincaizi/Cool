using System;
using DG.Tweening;
using Hotfix.GameSystems.UI.Framework.Animation;
using Hotfix.GameSystems.UI.Framework.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Components
{
    /// <summary>
    /// Confirm dialog component.
    /// Shows modal dialog with confirm/cancel actions.
    /// </summary>
    public class Confirm : UIPanel
    {
        [Header("Confirm UI References")]
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _messageText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Text _confirmText;
        [SerializeField] private Text _cancelText;

        private static Confirm _instance;
        private Action _onConfirm;
        private Action _onCancel;

        protected override string PrefabPath => "";
        protected override int Layer => UIConst.Layer_Popup;

        public static Confirm Instance
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

        private static Confirm CreateInstance()
        {
            var go = new GameObject("Confirm");
            var confirm = go.AddComponent<Confirm>();
            confirm.CreateLayout();

            confirm.BlockBack = true;
            confirm.CloseOnClickOutside = false;
            confirm.CanMultiOpen = false;
            confirm._useOpenAnim = true;
            confirm._useCloseAnim = true;

            return confirm;
        }

        private void CreateLayout()
        {
            var rt = GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(500, 300);

            var bg = CreateImage("Background");
            bg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            bg.rect.StretchParent();
            bg.rect.SetAsLastSibling();

            var content = new GameObject("Content");
            content.transform.SetParent(transform);

            _titleText = CreateText("Title", content.transform);
            _titleText.fontSize = 32;
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.color = Color.white;
            var titleRect = _titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.7f);
            titleRect.anchorMax = new Vector2(1, 1f);
            titleRect.sizeDelta = new Vector2(-40, 0);
            titleRect.anchoredPosition = new Vector2(0, 25);

            _messageText = CreateText("Message", content.transform);
            _messageText.fontSize = 24;
            _messageText.color = Color.white;
            _messageText.supportRichText = true;
            var msgRect = _messageText.GetComponent<RectTransform>();
            msgRect.anchorMin = new Vector2(0, 0.3f);
            msgRect.anchorMax = new Vector2(1, 0.7f);
            msgRect.sizeDelta = new Vector2(-40, 0);
            msgRect.anchoredPosition = new Vector2(0, 0);

            var btnContainer = new GameObject("Buttons");
            btnContainer.transform.SetParent(content.transform);

            _cancelButton = CreateButton("CancelBtn", btnContainer.transform);
            _cancelButton.GetComponent<Image>().color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var cancelRect = _cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = new Vector2(0.1f, 0.05f);
            cancelRect.anchorMax = new Vector2(0.45f, 0.25f);
            cancelRect.sizeDelta = Vector2.zero;
            _cancelText = _cancelButton.GetComponentInChildren<Text>();
            _cancelText.text = "Cancel";
            _cancelText.color = Color.white;

            _confirmButton = CreateButton("ConfirmBtn", btnContainer.transform);
            var confirmRect = _confirmButton.GetComponent<RectTransform>();
            confirmRect.anchorMin = new Vector2(0.55f, 0.05f);
            confirmRect.anchorMax = new Vector2(0.9f, 0.25f);
            confirmRect.sizeDelta = Vector2.zero;
            _confirmText = _confirmButton.GetComponentInChildren<Text>();
            _confirmText.text = "Confirm";
            _confirmText.color = Color.white;

            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        private Image CreateImage(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var img = go.AddComponent<Image>();
            img.raycastTarget = true;
            return img;
        }

        private Text CreateText(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var btn = go.AddComponent<Button>();
            var img = go.AddComponent<Image>();
            img.color = Color.white;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(go.transform);
            var txt = textObj.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.black;
            var txtRect = txtObj.GetComponent<RectTransform>();
            txtRect.StretchParent();

            return btn;
        }

        public static void Show(
            string title,
            string message,
            Action onConfirm,
            Action onCancel = null,
            string confirmText = "Confirm",
            string cancelText = "Cancel")
        {
            Instance.ShowDialog(title, message, onConfirm, onCancel, confirmText, cancelText);
        }

        private void ShowDialog(
            string title,
            string message,
            Action onConfirm,
            Action onCancel,
            string confirmText,
            string cancelText)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            _titleText.text = title;
            _messageText.text = message;
            _confirmText.text = confirmText;
            _cancelText.text = cancelText;

            UIManager.Instance.Open<Confirm>();
        }

        private void OnConfirmClicked()
        {
            _onConfirm?.Invoke();
            Close();
        }

        private void OnCancelClicked()
        {
            _onCancel?.Invoke();
            Close();
        }

        private void Close()
        {
            UIManager.Instance.Close<Confirm>();
            _onConfirm = null;
            _onCancel = null;
        }
    }
}

using System.Collections.Generic;
using System.Text;
using Cool.SyncCore;
using Hotfix.GameSystems.Bag.Data;
using Hotfix.GameSystems.Bag.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.Bag.UI
{
    internal sealed class WeaponBagView : MonoBehaviour
    {
        private RectTransform _panel;
        private Font _font;
        private string _lastSignature = string.Empty;

        public void Refresh(BagData data, long equippedId, long pendingId, string status, Font font)
        {
            bool open = _panel != null && _panel.gameObject.activeSelf;
            if (_panel == null)
            {
                _font = font;
                Build();
            }
            else if (font != null)
            {
                _font = font;
            }

            var rows = BuildRows(data, equippedId, pendingId);
            string signature = BuildSignature(rows, status);
            if (_lastSignature == signature)
            {
                _panel!.gameObject.SetActive(open);
                return;
            }
            _lastSignature = signature;

            foreach (Transform row in _panel!)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }

            int index = 0;
            foreach (var row in rows)
            {
                var button = AddButton(_panel, row.Label, new Vector2(0, -26 - index++ * 54),
                    () =>
                    {
                        if (row.Item != null) BagManager.Instance.RequestEquip(row.Item);
                    });
                button.interactable = row.Interactable;
            }

            if (!string.IsNullOrEmpty(status))
                CreateStatus(_panel, status, new Vector2(0, -26 - index++ * 54));

            _panel.sizeDelta = new Vector2(280, 16 + index * 54);
            _panel.gameObject.SetActive(open);
        }

        /// <summary>列表行模型：登录顺序、权威已装备标记与在途标记的唯一渲染口径。</summary>
        internal static WeaponBagRow[] BuildRows(BagData data, long equippedId, long pendingId)
        {
            var rows = new List<WeaponBagRow>();
            if (data != null)
            {
                foreach (var slot in data.Slots)
                {
                    var item = slot.Item;
                    if (item == null
                        || item.Template?.EquipSlot != Hotfix.GameSystems.Bag.Core.EquipmentSlot.Weapon)
                        continue;

                    bool equipped = item.InstanceId == equippedId;
                    bool pending = item.InstanceId == pendingId;
                    string label = SwordName(item.TemplateId);
                    if (equipped) label += "  [已装备]";
                    else if (pending) label += "  [换装中]";

                    rows.Add(new WeaponBagRow(item, label, equipped, pending,
                        !equipped && pendingId == 0));
                }
            }
            return rows.ToArray();
        }

        private static string BuildSignature(WeaponBagRow[] rows, string status)
        {
            var builder = new StringBuilder();
            builder.Append(status).Append('|');
            foreach (var row in rows)
            {
                builder.Append(row.Item.InstanceId).Append(':')
                    .Append(row.Label).Append(':')
                    .Append(row.Interactable ? '1' : '0').Append(';');
            }
            return builder.ToString();
        }

        private void Build()
        {
            var canvasObject = new GameObject("WeaponBagCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            canvasObject.AddComponent<GraphicRaycaster>();

            var root = (RectTransform)canvasObject.transform;
            var panel = new GameObject("Swords", typeof(RectTransform));
            _panel = (RectTransform)panel.transform;
            Place(_panel, root, new Vector2(-150, -72), new Vector2(280, 280));
            _panel.pivot = new Vector2(0.5f, 1);
            panel.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.12f, 0.96f);
            AddButton(root, "背包", new Vector2(-66, -42),
                () => panel.SetActive(!panel.activeSelf)).image.rectTransform.sizeDelta = new Vector2(108, 44);
        }

        private Button AddButton(RectTransform parent, string label, Vector2 position, UnityEngine.Events.UnityAction onClick)
        {
            var obj = new GameObject(label, typeof(RectTransform));
            var rect = (RectTransform)obj.transform;
            Place(rect, parent, position, new Vector2(256, 46));
            if (parent == _panel) rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            var image = obj.AddComponent<Image>();
            image.color = new Color(0.86f, 0.90f, 0.93f);
            var button = obj.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var caption = new GameObject("Label", typeof(RectTransform));
            var captionRect = (RectTransform)caption.transform;
            captionRect.SetParent(rect, false);
            captionRect.anchorMin = Vector2.zero;
            captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = captionRect.offsetMax = Vector2.zero;
            var text = caption.AddComponent<Text>();
            text.font = _font;
            text.fontSize = 19;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.09f, 0.14f, 0.18f);
            text.raycastTarget = false;
            text.text = label;
            return button;
        }

        private void CreateStatus(RectTransform parent, string status, Vector2 position)
        {
            var obj = new GameObject("Status", typeof(RectTransform));
            var rect = (RectTransform)obj.transform;
            Place(rect, parent, position, new Vector2(256, 46));
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            var text = obj.AddComponent<Text>();
            text.font = _font;
            text.fontSize = 17;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.86f, 0.4f);
            text.raycastTarget = false;
            text.text = status;
        }

        private static void Place(RectTransform rect, RectTransform parent, Vector2 position, Vector2 size)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static string SwordName(int templateId) => templateId switch
        {
            ElementalCatalogDefaults.DefaultSwordTemplateId => "默认剑",
            ElementalCatalogDefaults.IceSwordTemplateId => "冰剑",
            ElementalCatalogDefaults.FireSwordTemplateId => "火剑",
            ElementalCatalogDefaults.ThunderSwordTemplateId => "雷剑",
            ElementalCatalogDefaults.WindSwordTemplateId => "风剑",
            _ => "武器 " + templateId
        };
    }

    internal sealed class WeaponBagRow
    {
        public WeaponBagRow(ItemData item, string label, bool equipped, bool pending, bool interactable)
        {
            Item = item;
            Label = label;
            Equipped = equipped;
            Pending = pending;
            Interactable = interactable;
        }

        public ItemData Item { get; }
        public string Label { get; }
        public bool Equipped { get; }
        public bool Pending { get; }
        public bool Interactable { get; }
    }
}

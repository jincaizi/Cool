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

        public void Refresh(BagData data, long equippedId, Font font)
        {
            bool open = _panel != null && _panel.gameObject.activeSelf;
            if (_panel == null)
            {
                _font = font;
                Build();
            }
            foreach (Transform row in _panel)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }

            int index = 0;
            if (data != null)
            {
                foreach (var slot in data.Slots)
                {
                    var item = slot.Item;
                    if (item == null || item.Template?.EquipSlot != Hotfix.GameSystems.Bag.Core.EquipmentSlot.Weapon) continue;
                    bool equipped = item.InstanceId == equippedId;
                    var row = AddButton(_panel, SwordName(item.TemplateId) + (equipped ? "  [已装备]" : ""),
                        new Vector2(0, -26 - index++ * 54), () => BagManager.Instance.RequestEquip(item));
                    row.interactable = !equipped;
                }
            }
            _panel.sizeDelta = new Vector2(280, 16 + index * 54);
            _panel.gameObject.SetActive(open);
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
}

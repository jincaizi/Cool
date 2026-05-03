using UnityEngine;
using UnityEngine.UI;
using Hotfix.GameSystems.Bag.Data;
using Hotfix.GameSystems.Bag.Core;

namespace Hotfix.GameSystems.Bag.UI.Components
{
    /// <summary>
    /// 物品Tooltip组件
    /// </summary>
    public class ItemTooltip : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _typeText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Text _statsText;
        [SerializeField] private Text _requirementsText;
        [SerializeField] private Image _qualityIcon;

        [Header("Layout")]
        [SerializeField] private VerticalLayoutGroup _contentLayout;
        [SerializeField] private RectTransform _backgroundRect;

        private CanvasGroup _canvasGroup;
        private ItemData _currentItem;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            Hide();
        }

        /// <summary>
        /// 显示物品信息
        /// </summary>
        public void Show(ItemData item, Vector2 screenPosition)
        {
            if (item == null)
            {
                Hide();
                return;
            }

            _currentItem = item;
            var template = item.Template;

            // 基本信息
            if (_nameText != null)
            {
                _nameText.text = template?.Name ?? "Unknown Item";
                _nameText.color = GetQualityColor(template?.Quality ?? ItemQuality.White);
            }

            // 类型
            if (_typeText != null)
            {
                _typeText.text = GetTypeName(template?.Type ?? ItemType.Misc);
            }

            // 描述
            if (_descriptionText != null)
            {
                _descriptionText.text = template?.Description ?? "";
            }

            // 装备属性
            if (_statsText != null)
            {
                _statsText.text = GetStatsText(item);
            }

            // 需求
            if (_requirementsText != null)
            {
                _requirementsText.text = GetRequirementsText(template);
            }

            // 品质图标
            if (_qualityIcon != null)
            {
                _qualityIcon.color = GetQualityColor(template?.Quality ?? ItemQuality.White);
            }

            // 更新布局
            LayoutRebuilder.ForceRebuildLayoutImmediate(_backgroundRect);

            // 位置（防止超出屏幕）
            var pos = screenPosition;
            var canvasRect = GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                Vector2 localPos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out localPos);

                // 边界检测
                Vector2 size = _backgroundRect.sizeDelta;
                if (localPos.x + size.x > canvasRect.rect.width)
                    localPos.x = canvasRect.rect.width - size.x;
                if (localPos.y - size.y < 0)
                    localPos.y = size.y;

                transform.localPosition = localPos;
            }

            Show();
        }

        /// <summary>
        /// 隐藏
        /// </summary>
        public void Hide()
        {
            _currentItem = null;
            _canvasGroup.alpha = 0;
            gameObject.SetActive(false);
        }

        private void Show()
        {
            gameObject.SetActive(true);
            _canvasGroup.alpha = 1;
        }

        private string GetStatsText(ItemData item)
        {
            if (item == null) return "";

            var sb = new System.Text.StringBuilder();
            var template = item.Template;

            if (template == null) return "";

            switch (template.Type)
            {
                case ItemType.Equipment:
                    if (template.BaseAttack > 0)
                        sb.AppendLine($"攻击力: +{template.BaseAttack + item.BonusAttack}");
                    if (template.BaseDefense > 0)
                        sb.AppendLine($"防御力: +{template.BaseDefense + item.BonusDefense}");
                    if (template.BaseHp > 0)
                        sb.AppendLine($"生命值: +{template.BaseHp}");
                    if (template.BaseCritRate > 0)
                        sb.AppendLine($"暴击率: +{template.BaseCritRate * 100:F1}%");
                    if (item.Level > 0)
                        sb.AppendLine($"强化等级: +{item.Level}");
                    break;

                case ItemType.Consumable:
                    if (template.UseEffectId > 0 && template.UseValue > 0)
                        sb.AppendLine($"效果: +{template.UseValue}");
                    break;

                case ItemType.Material:
                    sb.AppendLine("材料物品");
                    break;

                default:
                    break;
            }

            // 耐久度
            if (template.Type == ItemType.Equipment && item.MaxDurability > 0)
            {
                sb.AppendLine($"耐久度: {item.Durability}/{item.MaxDurability}");
            }

            return sb.ToString().TrimEnd();
        }

        private string GetRequirementsText(ItemTemplate template)
        {
            if (template == null) return "";

            var sb = new System.Text.StringBuilder();

            if (template.LevelRequire > 0)
            {
                sb.AppendLine($"等级需求: {template.LevelRequire}");
            }

            if (template.EquipSlot != EquipmentSlot.None)
            {
                sb.AppendLine($"装备栏: {GetEquipSlotName(template.EquipSlot)}");
            }

            return sb.ToString().TrimEnd();
        }

        private string GetTypeName(ItemType type)
        {
            return type switch
            {
                ItemType.Equipment => "装备",
                ItemType.Consumable => "消耗品",
                ItemType.Material => "材料",
                ItemType.QuestItem => "任务物品",
                ItemType.Currency => "货币",
                ItemType.Misc => "杂物",
                _ => "未知",
            };
        }

        private string GetEquipSlotName(EquipmentSlot slot)
        {
            return slot switch
            {
                EquipmentSlot.Weapon => "武器",
                EquipmentSlot.Head => "头部",
                EquipmentSlot.Chest => "胸部",
                EquipmentSlot.Legs => "腿部",
                EquipmentSlot.Boots => "靴子",
                EquipmentSlot.Gloves => "手套",
                EquipmentSlot.Ring => "戒指",
                EquipmentSlot.Necklace => "项链",
                EquipmentSlot.Cape => "披风",
                _ => "无",
            };
        }

        private Color GetQualityColor(ItemQuality quality)
        {
            return quality switch
            {
                ItemQuality.White => new Color(0.9f, 0.9f, 0.9f),
                ItemQuality.Green => new Color(0.2f, 1f, 0.2f),
                ItemQuality.Blue => new Color(0.3f, 0.7f, 1f),
                ItemQuality.Purple => new Color(0.9f, 0.4f, 1f),
                ItemQuality.Orange => new Color(1f, 0.7f, 0.2f),
                _ => Color.white,
            };
        }
    }
}
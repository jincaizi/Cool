using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Hotfix.GameSystems.Bag.Data;

namespace Hotfix.GameSystems.Bag.UI.Components
{
    /// <summary>
    /// 背包格子组件
    /// </summary>
    public class ItemCell : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image _iconImage;
        [SerializeField] private Text _countText;
        [SerializeField] private Image _qualityBorder;
        [SerializeField] private Image _lockIcon;
        [SerializeField] private GameObject _emptyBg;
        [SerializeField] private GameObject _itemBg;

        public int SlotIndex { get; set; }
        public ItemData CurrentItem { get; private set; }

        // 事件
        public System.Action<int, ItemData> OnItemClick;        // 格子索引, 物品
        public System.Action<int, ItemData> OnItemRightClick;
        public System.Action<int, ItemData> OnItemHover;        // 进入
        public System.Action<int, ItemData> OnItemHoverEnd;     // 离开

        private void Awake()
        {
            Clear();
        }

        /// <summary>
        /// 设置物品
        /// </summary>
        public void SetItem(ItemData item)
        {
            CurrentItem = item;

            if (item == null)
            {
                Clear();
                return;
            }

            _emptyBg?.SetActive(false);
            _itemBg?.SetActive(true);
            _lockIcon?.SetActive(item.IsLocked);

            // 设置数量
            if (_countText != null)
            {
                _countText.gameObject.SetActive(item.CanStack && item.Count > 1);
                _countText.text = item.Count.ToString();
            }

            // 设置品质边框颜色
            if (_qualityBorder != null)
            {
                _qualityBorder.gameObject.SetActive(true);
                _qualityBorder.color = GetQualityColor(item.Template?.Quality ?? Core.ItemQuality.White);
            }

            // 加载图标（TODO: 使用Addressable）
            if (_iconImage != null)
            {
                // _iconImage.sprite = Resources.Load<Sprite>(item.Template?.Icon);
                _iconImage.color = Color.white;
            }
        }

        /// <summary>
        /// 清空格子
        /// </summary>
        public void Clear()
        {
            CurrentItem = null;
            _emptyBg?.SetActive(true);
            _itemBg?.SetActive(false);
            _lockIcon?.SetActive(false);

            if (_countText != null)
                _countText.gameObject.SetActive(false);

            if (_qualityBorder != null)
                _qualityBorder.gameObject.SetActive(false);

            if (_iconImage != null)
                _iconImage.color = Color.clear;
        }

        /// <summary>
        /// 设置是否高亮（用于拖拽操作）
        /// </summary>
        public void SetHighlight(bool highlight)
        {
            var colors = _iconImage?.color ?? Color.white;
            if (highlight)
            {
                colors.a = 0.5f;
            }
            else
            {
                colors.a = 1f;
            }
            if (_iconImage != null)
                _iconImage.color = colors;
        }

        /// <summary>
        /// 设置可放置状态
        /// </summary>
        public void SetCanPlace(bool canPlace)
        {
            if (_itemBg != null)
            {
                _itemBg.SetActive(true);
                var image = _itemBg.GetComponent<Image>();
                if (image != null)
                {
                    image.color = canPlace ? new Color(0.3f, 1f, 0.3f, 0.3f) : new Color(1f, 0.3f, 0.3f, 0.3f);
                }
            }
        }

        private Color GetQualityColor(Core.ItemQuality quality)
        {
            return quality switch
            {
                Core.ItemQuality.White => new Color(1f, 1f, 1f, 0.8f),
                Core.ItemQuality.Green => new Color(0.2f, 1f, 0.2f, 0.8f),
                Core.ItemQuality.Blue => new Color(0.2f, 0.6f, 1f, 0.8f),
                Core.ItemQuality.Purple => new Color(0.8f, 0.2f, 1f, 0.8f),
                Core.ItemQuality.Orange => new Color(1f, 0.6f, 0f, 0.8f),
                _ => new Color(1f, 1f, 1f, 0.8f),
            };
        }

        // ==================== 事件处理 ====================

        public void OnPointerClick(PointerEventData eventData)
        {
            OnItemClick?.Invoke(SlotIndex, CurrentItem);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OnItemHover?.Invoke(SlotIndex, CurrentItem);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OnItemHoverEnd?.Invoke(SlotIndex, CurrentItem);
        }

        public void OnRightClick()
        {
            OnItemRightClick?.Invoke(SlotIndex, CurrentItem);
        }
    }
}
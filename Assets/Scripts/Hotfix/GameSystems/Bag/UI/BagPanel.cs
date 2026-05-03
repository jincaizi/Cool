using UnityEngine;
using UnityEngine.UI;
using Hotfix.GameSystems.Bag.Runtime;
using Hotfix.GameSystems.Bag.Data;
using Hotfix.GameSystems.Bag.Core;
using Hotfix.GameSystems.Bag.Core.Events;
using Hotfix.GameSystems.Bag.UI.Components;

namespace Hotfix.GameSystems.Bag.UI
{
    /// <summary>
    /// 背包面板
    /// </summary>
    public class BagPanel : MonoBehaviour
    {
        [Header("Bag Settings")]
        [SerializeField] private int _rowCount = BagData.DefaultRowCount;
        [SerializeField] private int _columnCount = BagData.DefaultColumnCount;

        [Header("UI References")]
        [SerializeField] private GridLayoutGroup _gridLayout;
        [SerializeField] private ItemCell _cellPrefab;
        [SerializeField] private ItemTooltip _tooltip;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _slotCountText;

        [Header("Actions")]
        [SerializeField] private Button _sortButton;
        [SerializeField] private Button _expandButton;

        private ItemCell[] _cells;
        private ItemData _draggingItem;
        private int _draggingSlotIndex = -1;

        private void Awake()
        {
            // 初始化格子
            InitializeGrid();

            // 注册事件
            RegisterEvents();
        }

        private void OnEnable()
        {
            RefreshAllCells();
            UpdateSlotCount();
        }

        private void OnDisable()
        {
            _tooltip?.Hide();
        }

        private void OnDestroy()
        {
            UnregisterEvents();
        }

        private void RegisterEvents()
        {
            BagManager.Instance.OnItemAdded += OnItemAdded;
            BagManager.Instance.OnItemRemoved += OnItemRemoved;
            BagManager.Instance.OnItemMoved += OnItemMoved;
            BagManager.Instance.OnItemStacked += OnItemStacked;
            BagManager.Instance.OnItemUsed += OnItemUsed;
        }

        private void UnregisterEvents()
        {
            if (BagManager.Instance != null)
            {
                BagManager.Instance.OnItemAdded -= OnItemAdded;
                BagManager.Instance.OnItemRemoved -= OnItemRemoved;
                BagManager.Instance.OnItemMoved -= OnItemMoved;
                BagManager.Instance.OnItemStacked -= OnItemStacked;
                BagManager.Instance.OnItemUsed -= OnItemUsed;
            }
        }

        private void InitializeGrid()
        {
            if (_gridLayout == null || _cellPrefab == null) return;

            // 计算格子大小
            int totalCells = _rowCount * _columnCount;
            RectTransform gridRect = _gridLayout.GetComponent<RectTransform>();
            Vector2 gridSize = gridRect.sizeDelta;
            float spacing = _gridLayout.spacing.x;
            float padding = _gridLayout.padding.left + _gridLayout.padding.right;

            float cellWidth = (gridSize.x - padding - spacing * (_columnCount - 1)) / _columnCount;
            float cellHeight = (gridSize.y - _gridLayout.padding.top - _gridLayout.padding.bottom - spacing * (_rowCount - 1)) / _rowCount;

            _gridLayout.cellSize = new Vector2(cellWidth, cellHeight);

            // 创建格子
            _cells = new ItemCell[totalCells];
            for (int i = 0; i < totalCells; i++)
            {
                var cell = Instantiate(_cellPrefab, _gridLayout.transform);
                cell.SlotIndex = i;
                cell.name = $"Cell_{i}";
                cell.OnItemClick += OnCellClick;
                cell.OnItemRightClick += OnCellRightClick;
                cell.OnItemHover += OnCellHover;
                cell.OnItemHoverEnd += OnCellHoverEnd;
                _cells[i] = cell;
            }
        }

        private void OnCellClick(int slotIndex, ItemData item)
        {
            if (item == null) return;

            // 双击使用物品
            if (item.CanUse)
            {
                BagManager.Instance.UseItem(slotIndex);
            }
            // TODO: 装备穿戴
        }

        private void OnCellRightClick(int slotIndex, ItemData item)
        {
            if (item == null) return;

            // 右键使用
            if (item.CanUse)
            {
                BagManager.Instance.UseItem(slotIndex);
            }
            // TODO: 右键菜单（丢弃、出售等）
        }

        private void OnCellHover(int slotIndex, ItemData item)
        {
            if (item == null) return;

            Vector2 screenPos = Input.mousePosition;
            _tooltip?.Show(item, screenPos);
        }

        private void OnCellHoverEnd(int slotIndex, ItemData item)
        {
            _tooltip?.Hide();
        }

        // ==================== 事件回调 ====================

        private void OnItemAdded(ItemAddedEvent e)
        {
            RefreshCell(e.SlotIndex);
            UpdateSlotCount();
        }

        private void OnItemRemoved(ItemRemovedEvent e)
        {
            RefreshCell(e.SlotIndex);
            UpdateSlotCount();
        }

        private void OnItemMoved(ItemMovedEvent e)
        {
            RefreshCell(e.FromSlot);
            RefreshCell(e.ToSlot);
        }

        private void OnItemStacked(ItemStackedEvent e)
        {
            RefreshCell(e.SlotIndex);
        }

        private void OnItemUsed(ItemUsedEvent e)
        {
            RefreshCell(e.SlotIndex);
        }

        // ==================== 刷新 ====================

        private void RefreshCell(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _cells.Length) return;

            var item = BagManager.Instance.GetItem(slotIndex);
            _cells[slotIndex].SetItem(item);
        }

        private void RefreshAllCells()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                RefreshCell(i);
            }
        }

        private void UpdateSlotCount()
        {
            if (_slotCountText == null) return;

            var data = BagManager.Instance.Data;
            int used = data.ItemCount;
            int total = data.TotalCapacity;
            _slotCountText.text = $"{used}/{total}";
        }

        // ==================== 按钮事件 ====================

        public void OnSortButtonClick()
        {
            BagManager.Instance.Compact();
            RefreshAllCells();
        }

        public void OnExpandButtonClick()
        {
            BagManager.Instance.ExpandCapacity(10);
            RefreshAllCells();
            UpdateSlotCount();
        }

        public void OnCloseButtonClick()
        {
            Hide();
        }

        // ==================== 快捷键 ====================

        private bool IsVisible => gameObject.activeSelf;

        private void Show()
        {
            gameObject.SetActive(true);
            RefreshAllCells();
            UpdateSlotCount();
        }

        private void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Update()
        {
            // B键打开/关闭背包
            if (UnityEngine.Input.GetKeyDown(KeyCode.B))
            {
                if (IsVisible)
                    Hide();
                else
                    Show();
            }

            // ESC关闭
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && IsVisible)
            {
                Hide();
            }
        }
    }
}
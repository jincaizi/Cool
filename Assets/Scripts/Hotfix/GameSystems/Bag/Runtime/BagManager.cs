using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Bag.Core;
using Hotfix.GameSystems.Bag.Core.Events;
using Hotfix.GameSystems.Bag.Data;

namespace Hotfix.GameSystems.Bag.Runtime
{
    /// <summary>
    /// 背包管理器
    /// </summary>
    public class BagManager
    {
        private static BagManager _instance;
        public static BagManager Instance => _instance ??= new BagManager();

        private BagData _bagData;
        private bool _isInitialized;

        public BagData Data => _bagData;
        public bool IsInitialized => _isInitialized;

        // 事件回调
        public event Action<ItemAddedEvent> OnItemAdded;
        public event Action<ItemRemovedEvent> OnItemRemoved;
        public event Action<ItemMovedEvent> OnItemMoved;
        public event Action<ItemStackedEvent> OnItemStacked;
        public event Action<ItemUsedEvent> OnItemUsed;
        public event Action<ItemLockChangedEvent> OnItemLockChanged;

        private BagManager()
        {
        }

        /// <summary>
        /// 初始化背包
        /// </summary>
        public void Initialize(int capacity = BagData.DefaultCapacity)
        {
            _bagData = new BagData
            {
                Capacity = capacity
            };
            _bagData.Initialize();
            _isInitialized = true;
            UnityEngine.Debug.Log($"[Bag] BagManager initialized with {capacity} slots");
        }

        /// <summary>
        /// 从服务器数据加载
        /// </summary>
        public void LoadFromServer(BagData data)
        {
            _bagData = data ?? new BagData();
            if (_bagData.Slots.Count == 0)
            {
                _bagData.Initialize();
            }
            _isInitialized = true;
            UnityEngine.Debug.Log($"[Bag] Bag loaded from server, {data.ItemCount} items");
        }

        /// <summary>
        /// 添加物品
        /// </summary>
        public (BagResult result, int slotIndex, ItemData item) AddItem(int templateId, int count = 1)
        {
            if (!_isInitialized || _bagData == null)
                return (BagResult.InvalidItem, -1, null);

            var template = ItemTemplateRegistry.Get(templateId);
            if (template == null)
                return (BagResult.InvalidItem, -1, null);

            // 尝试堆叠到已有物品
            if (template.MaxStack > 1)
            {
                int existingSlot = _bagData.FindItemByTemplateId(templateId);
                while (existingSlot >= 0 && count > 0)
                {
                    var slot = _bagData.GetSlot(existingSlot);
                    if (slot != null && slot.Item != null)
                    {
                        int space = slot.Item.StackSpace;
                        if (space > 0)
                        {
                            int addCount = System.Math.Min(count, space);
                            int oldCount = slot.Item.Count;
                            slot.Item.Count += addCount;
                            count -= addCount;

                            // 触发堆叠事件
                            var stackedEvent = new ItemStackedEvent(slot.Item.InstanceId, existingSlot, oldCount, slot.Item.Count);
                            OnItemStacked?.Invoke(stackedEvent);
                            // Event stored in OnItemStacked callback
                        //EventBus.Emit(stackedEvent);
                        }
                    }

                    // 继续找下一个可堆叠的格子
                    existingSlot = _bagData.FindItemByTemplateId(templateId);
                    // 跳过已满的格子
                    for (int i = existingSlot + 1; i < _bagData.Slots.Count; i++)
                    {
                        if (!_bagData.GetSlot(i).IsEmpty &&
                            _bagData.GetSlot(i).Item.TemplateId == templateId &&
                            _bagData.GetSlot(i).Item.StackSpace > 0)
                        {
                            existingSlot = i;
                            break;
                        }
                        existingSlot = -1;
                    }
                }
            }

            // 剩余数量放入新格子
            while (count > 0)
            {
                if (_bagData.IsFull)
                    return (BagResult.Full, -1, null);

                var item = ItemData.Create(templateId, count);
                if (item == null)
                    return (BagResult.InvalidItem, -1, null);

                // 找到空格子
                int emptySlot = -1;
                for (int i = 0; i < _bagData.Slots.Count; i++)
                {
                    if (_bagData.GetSlot(i).IsEmpty)
                    {
                        emptySlot = i;
                        break;
                    }
                }

                if (emptySlot < 0)
                    return (BagResult.Full, -1, null);

                // 放入物品
                int actualCount = System.Math.Min(count, item.MaxStackCount);
                item.Count = actualCount;
                _bagData.GetSlot(emptySlot).SetItem(item);
                count -= actualCount;

                // 触发添加事件
                var addEvent = new ItemAddedEvent(item, emptySlot, item.Count);
                OnItemAdded?.Invoke(addEvent);
                // Event stored in OnItemAdded callback
                // EventBus.Emit(addEvent);
            }

            return (BagResult.Success, 0, null);
        }

        /// <summary>
        /// 移除物品
        /// </summary>
        public BagResult RemoveItem(long instanceId, int count = 1)
        {
            if (!_isInitialized || _bagData == null)
                return BagResult.InvalidItem;

            int slotIndex = _bagData.FindItemSlot(instanceId);
            if (slotIndex < 0)
                return BagResult.InvalidItem;

            var slot = _bagData.GetSlot(slotIndex);
            if (slot.IsEmpty || slot.Item == null)
                return BagResult.InvalidItem;

            if (count > slot.Item.Count)
                count = slot.Item.Count;

            slot.Item.Count -= count;

            // 触发移除事件
            var removeEvent = new ItemRemovedEvent(slot.Item.TemplateId, count, slotIndex);
            OnItemRemoved?.Invoke(removeEvent);
            //EventBus.Emit(removeEvent);

            // 如果数量为0，清空格子
            if (slot.Item.Count <= 0)
            {
                slot.Clear();
            }

            return BagResult.Success;
        }

        /// <summary>
        /// 移动物品
        /// </summary>
        public BagResult MoveItem(int fromSlot, int toSlot)
        {
            if (!_isInitialized || _bagData == null)
                return BagResult.InvalidItem;

            if (!_bagData.IsValidIndex(fromSlot) || !_bagData.IsValidIndex(toSlot))
                return BagResult.InvalidItem;

            if (fromSlot == toSlot)
                return BagResult.Success;

            var fromSlotData = _bagData.GetSlot(fromSlot);
            var toSlotData = _bagData.GetSlot(toSlot);

            // 目标格子为空，直接移动
            if (toSlotData.IsEmpty)
            {
                toSlotData.SetItem(fromSlotData.Item);
                fromSlotData.Clear();

                var moveEvent = new ItemMovedEvent(toSlotData.Item?.InstanceId ?? 0, fromSlot, toSlot);
                OnItemMoved?.Invoke(moveEvent);
                //EventBus.Emit(moveEvent);
            }
            else
            {
                // 目标格子有物品，尝试堆叠或交换
                var fromItem = fromSlotData.Item;
                var toItem = toSlotData.Item;

                // 如果是同一种物品且可堆叠
                if (fromItem.TemplateId == toItem.TemplateId && fromItem.CanStack)
                {
                    int space = toItem.StackSpace;
                    if (space > 0)
                    {
                        int moveCount = System.Math.Min(space, fromItem.Count);
                        toItem.Count += moveCount;
                        fromItem.Count -= moveCount;

                        var stackedEvent = new ItemStackedEvent(toItem.InstanceId, toSlot, toItem.Count - moveCount, toItem.Count);
                        OnItemStacked?.Invoke(stackedEvent);
                        // Event stored in OnItemStacked callback
                        //EventBus.Emit(stackedEvent);

                        if (fromItem.Count <= 0)
                        {
                            fromSlotData.Clear();
                        }
                    }
                }
                else
                {
                    // 交换位置
                    fromSlotData.SetItem(toItem);
                    toSlotData.SetItem(fromItem);

                    var moveEvent = new ItemMovedEvent(fromItem.InstanceId, fromSlot, toSlot);
                    OnItemMoved?.Invoke(moveEvent);
                    //EventBus.Emit(moveEvent);
                }
            }

            return BagResult.Success;
        }

        /// <summary>
        /// 使用物品
        /// </summary>
        public BagResult UseItem(int slotIndex)
        {
            if (!_isInitialized || _bagData == null)
                return BagResult.InvalidItem;

            var slot = _bagData.GetSlot(slotIndex);
            if (slot?.Item == null)
                return BagResult.InvalidItem;

            var item = slot.Item;

            // 检查是否可使用
            if (!item.CanUse)
                return BagResult.InvalidItem;

            // 执行使用逻辑
            bool success = false;

            // TODO: 根据物品类型执行不同的使用效果
            switch (item.Template?.Type)
            {
                case ItemType.Consumable:
                    // 消耗品效果
                    success = true;
                    break;
            }

            // 触发使用事件
            var useEvent = new ItemUsedEvent(item, slotIndex, success);
            OnItemUsed?.Invoke(useEvent);
            //EventBus.Emit(useEvent);

            if (success)
            {
                // 减少物品数量
                item.Count--;
                if (item.Count <= 0)
                {
                    slot.Clear();
                }
            }

            return success ? BagResult.Success : BagResult.InvalidItem;
        }

        /// <summary>
        /// 锁定/解锁物品
        /// </summary>
        public void SetItemLock(int slotIndex, bool locked)
        {
            var slot = _bagData.GetSlot(slotIndex);
            if (slot?.Item == null) return;

            slot.Item.IsLocked = locked;
            slot.IsLocked = locked;

            var lockEvent = new ItemLockChangedEvent(slot.Item.InstanceId, slotIndex, locked);
            OnItemLockChanged?.Invoke(lockEvent);
            //EventBus.Emit(lockEvent);
        }

        /// <summary>
        /// 获取指定格子的物品
        /// </summary>
        public ItemData GetItem(int slotIndex)
        {
            return _bagData?.GetSlot(slotIndex)?.Item;
        }

        /// <summary>
        /// 扩展背包容量
        /// </summary>
        public BagResult ExpandCapacity(int addSlots)
        {
            if (addSlots <= 0)
                return BagResult.InvalidItem;

            int oldCapacity = _bagData.TotalCapacity;
            _bagData.ExtendedCapacity += addSlots;

            // 添加新格子
            for (int i = 0; i < addSlots; i++)
            {
                _bagData.Slots.Add(new BagSlotData(oldCapacity + i));
            }

            var capacityEvent = new BagCapacityChangedEvent(oldCapacity, _bagData.TotalCapacity);
            //EventBus.Emit(capacityEvent);

            return BagResult.Success;
        }

        /// <summary>
        /// 整理背包（将物品压缩到前面）
        /// </summary>
        public void Compact()
        {
            if (!_isInitialized || _bagData == null)
                return;

            var items = new List<ItemData>();
            foreach (var slot in _bagData.Slots)
            {
                if (!slot.IsEmpty && slot.Item != null)
                {
                    items.Add(slot.Item);
                }
            }

            _bagData.Initialize();
            _bagData.ExtendedCapacity = 0; // 重置扩展容量

            foreach (var item in items)
            {
                AddItem(item.TemplateId, item.Count);
            }
        }

        /// <summary>
        /// 清空背包
        /// </summary>
        public void Clear()
        {
            _bagData?.Initialize();
        }
    }
}
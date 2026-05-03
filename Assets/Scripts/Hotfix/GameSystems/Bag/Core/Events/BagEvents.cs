using Hotfix.GameSystems.Bag.Core;
using Hotfix.GameSystems.Bag.Data;

namespace Hotfix.GameSystems.Bag.Core.Events
{
    /// <summary>
    /// 物品添加事件
    /// </summary>
    public class ItemAddedEvent : IBagEvent
    {
        public ItemData Item { get; }
        public int SlotIndex { get; }
        public int TotalCount { get; }

        public ItemAddedEvent(ItemData item, int slotIndex, int totalCount)
        {
            Item = item;
            SlotIndex = slotIndex;
            TotalCount = totalCount;
        }
    }

    /// <summary>
    /// 物品移除事件
    /// </summary>
    public class ItemRemovedEvent : IBagEvent
    {
        public int TemplateId { get; }
        public int Count { get; }
        public int SlotIndex { get; }

        public ItemRemovedEvent(int templateId, int count, int slotIndex)
        {
            TemplateId = templateId;
            Count = count;
            SlotIndex = slotIndex;
        }
    }

    /// <summary>
    /// 物品移动事件
    /// </summary>
    public class ItemMovedEvent : IBagEvent
    {
        public long InstanceId { get; }
        public int FromSlot { get; }
        public int ToSlot { get; }

        public ItemMovedEvent(long instanceId, int fromSlot, int toSlot)
        {
            InstanceId = instanceId;
            FromSlot = fromSlot;
            ToSlot = toSlot;
        }
    }

    /// <summary>
    /// 物品堆叠事件
    /// </summary>
    public class ItemStackedEvent : IBagEvent
    {
        public long InstanceId { get; }
        public int SlotIndex { get; }
        public int OldCount { get; }
        public int NewCount { get; }

        public ItemStackedEvent(long instanceId, int slotIndex, int oldCount, int newCount)
        {
            InstanceId = instanceId;
            SlotIndex = slotIndex;
            OldCount = oldCount;
            NewCount = newCount;
        }
    }

    /// <summary>
    /// 物品使用事件
    /// </summary>
    public class ItemUsedEvent : IBagEvent
    {
        public ItemData Item { get; }
        public int SlotIndex { get; }
        public bool Success { get; }

        public ItemUsedEvent(ItemData item, int slotIndex, bool success)
        {
            Item = item;
            SlotIndex = slotIndex;
            Success = success;
        }
    }

    /// <summary>
    /// 物品锁定/解锁事件
    /// </summary>
    public class ItemLockChangedEvent : IBagEvent
    {
        public long InstanceId { get; }
        public int SlotIndex { get; }
        public bool IsLocked { get; }

        public ItemLockChangedEvent(long instanceId, int slotIndex, bool isLocked)
        {
            InstanceId = instanceId;
            SlotIndex = slotIndex;
            IsLocked = isLocked;
        }
    }

    /// <summary>
    /// 背包容量变化事件
    /// </summary>
    public class BagCapacityChangedEvent : IBagEvent
    {
        public int OldCapacity { get; }
        public int NewCapacity { get; }

        public BagCapacityChangedEvent(int oldCapacity, int newCapacity)
        {
            OldCapacity = oldCapacity;
            NewCapacity = newCapacity;
        }
    }

    /// <summary>
    /// 背包打开/关闭事件
    /// </summary>
    public class BagOpenChangedEvent : IBagEvent
    {
        public bool IsOpen { get; }

        public BagOpenChangedEvent(bool isOpen)
        {
            IsOpen = isOpen;
        }
    }
}
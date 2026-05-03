using Hotfix.GameSystems.Bag.Core;

namespace Hotfix.GameSystems.Bag.Data
{
    /// <summary>
    /// 背包格子数据
    /// </summary>
    public class BagSlotData
    {
        public int Index { get; set; }          // 格子索引
        public ItemData Item { get; set; }      // 格子中的物品（null表示空）
        public bool IsEmpty => Item == null;
        public bool IsLocked { get; set; }      // 是否锁定（防止误操作）

        public BagSlotData()
        {
        }

        public BagSlotData(int index)
        {
            Index = index;
        }

        /// <summary>
        /// 清空格子
        /// </summary>
        public void Clear()
        {
            Item = null;
        }

        /// <summary>
        /// 设置物品
        /// </summary>
        public void SetItem(ItemData item)
        {
            Item = item;
            if (item != null)
            {
                item.SlotIndex = Index;
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace Hotfix.GameSystems.Bag.Data
{
    /// <summary>
    /// 背包数据
    /// </summary>
    [Serializable]
    public class BagData
    {
        public const int DefaultRowCount = 20;
        public const int DefaultColumnCount = 4;
        public const int DefaultCapacity = DefaultRowCount * DefaultColumnCount; // 80格

        /// <summary>
        /// 背包容量（格子总数）
        /// </summary>
        public int Capacity { get; set; } = DefaultCapacity;

        /// <summary>
        /// 扩展容量（额外解锁的格子数）
        /// </summary>
        public int ExtendedCapacity { get; set; } = 0;

        /// <summary>
        /// 实际可用容量
        /// </summary>
        public int TotalCapacity => Capacity + ExtendedCapacity;

        /// <summary>
        /// 格子数据列表
        /// </summary>
        public List<BagSlotData> Slots { get; set; } = new();

        /// <summary>
        /// 初始化背包
        /// </summary>
        public void Initialize()
        {
            Slots.Clear();
            for (int i = 0; i < TotalCapacity; i++)
            {
                Slots.Add(new BagSlotData(i));
            }
        }

        /// <summary>
        /// 获取指定索引的格子
        /// </summary>
        public BagSlotData GetSlot(int index)
        {
            if (index < 0 || index >= Slots.Count)
                return null;
            return Slots[index];
        }

        /// <summary>
        /// 检查索引是否有效
        /// </summary>
        public bool IsValidIndex(int index)
        {
            return index >= 0 && index < Slots.Count;
        }

        /// <summary>
        /// 获取空格子数量
        /// </summary>
        public int EmptySlotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Slots[i].IsEmpty)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 获取物品总数
        /// </summary>
        public int ItemCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (!Slots[i].IsEmpty)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 是否已满
        /// </summary>
        public bool IsFull => EmptySlotCount == 0;

        /// <summary>
        /// 找到指定物品实例所在的格子索引
        /// </summary>
        public int FindItemSlot(long instanceId)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (!Slots[i].IsEmpty && Slots[i].Item.InstanceId == instanceId)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 找到第一个指定模板ID的物品格子
        /// </summary>
        public int FindItemByTemplateId(int templateId)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (!Slots[i].IsEmpty && Slots[i].Item.TemplateId == templateId)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 获取所有指定类型的物品格子
        /// </summary>
        public List<int> FindItemsByType(Core.ItemType type)
        {
            var result = new List<int>();
            for (int i = 0; i < Slots.Count; i++)
            {
                if (!Slots[i].IsEmpty && Slots[i].Item.Template?.Type == type)
                    result.Add(i);
            }
            return result;
        }

        /// <summary>
        /// 序列化数据（用于网络传输/存储）
        /// </summary>
        public byte[] Serialize()
        {
            // TODO: 使用Protobuf或MessagePack序列化
            return null;
        }

        /// <summary>
        /// 反序列化数据
        /// </summary>
        public static BagData Deserialize(byte[] data)
        {
            // TODO: 使用Protobuf或MessagePack反序列化
            return null;
        }
    }
}

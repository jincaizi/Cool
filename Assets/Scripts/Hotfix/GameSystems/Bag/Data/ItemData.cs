using Hotfix.GameSystems.Bag.Core;

namespace Hotfix.GameSystems.Bag.Data
{
    /// <summary>
    /// 物品实例数据（运行时使用）
    /// </summary>
    public class ItemData
    {
        public long InstanceId { get; set; }     // 唯一实例ID（服务器生成）
        public int TemplateId { get; set; }     // 模板ID
        public int Count { get; set; }          // 数量
        public int SlotIndex { get; set; }      // 当前所在格子索引
        public bool IsLocked { get; set; }       // 是否锁定
        public long CreateTime { get; set; }     // 创建时间戳
        public long ExpireTime { get; set; }     // 过期时间（0表示永不过期）

        // 装备额外属性（用于装备强化等）
        public int Level { get; set; }          // 强化等级
        public int Durability { get; set; }      // 耐久度
        public int MaxDurability { get; set; }   // 最大耐久度

        // 属性加成（强化后累加）
        public int BonusAttack { get; set; }
        public int BonusDefense { get; set; }

        /// <summary>
        /// 获取物品模板
        /// </summary>
        public ItemTemplate Template => ItemTemplateRegistry.Get(TemplateId);

        /// <summary>
        /// 是否可堆叠
        /// </summary>
        public bool CanStack => Template != null && Template.MaxStack > 1;

        /// <summary>
        /// 是否已过期
        /// </summary>
        public bool IsExpired => ExpireTime > 0 && ExpireTime < GetCurrentTimestamp();

        /// <summary>
        /// 是否可使用
        /// </summary>
        public bool CanUse => Template?.Type == ItemType.Consumable && !IsExpired;

        /// <summary>
        /// 获取最大堆叠数
        /// </summary>
        public int MaxStackCount => Template?.MaxStack ?? 1;

        /// <summary>
        /// 剩余堆叠空间
        /// </summary>
        public int StackSpace => MaxStackCount - Count;

        /// <summary>
        /// 创建物品实例
        /// </summary>
        public static ItemData Create(int templateId, int count = 1)
        {
            var template = ItemTemplateRegistry.Get(templateId);
            if (template == null)
            {
                UnityEngine.Debug.LogWarning($"[Bag] ItemTemplate not found: {templateId}");
                return null;
            }

            return new ItemData
            {
                InstanceId = GenerateInstanceId(),
                TemplateId = templateId,
                Count = System.Math.Min(count, template.MaxStack),
                CreateTime = GetCurrentTimestamp(),
                Level = 0,
            };
        }

        /// <summary>
        /// 复制物品（用于拆分）
        /// </summary>
        public ItemData Clone()
        {
            return new ItemData
            {
                InstanceId = GenerateInstanceId(),
                TemplateId = TemplateId,
                Count = 0, // 数量由调用者设置
                CreateTime = CreateTime,
                ExpireTime = ExpireTime,
                Level = Level,
                Durability = Durability,
                MaxDurability = MaxDurability,
                BonusAttack = BonusAttack,
                BonusDefense = BonusDefense,
            };
        }

        private static long _lastInstanceId = 0;
        private static long GenerateInstanceId()
        {
            return System.Threading.Interlocked.Increment(ref _lastInstanceId);
        }

        private static long GetCurrentTimestamp()
        {
            return System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public override string ToString()
        {
            var name = Template?.Name ?? $"Unknown({TemplateId})";
            return CanStack ? $"{name} x{Count}" : name;
        }
    }
}

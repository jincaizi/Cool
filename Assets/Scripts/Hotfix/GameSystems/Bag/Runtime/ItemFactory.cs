using Hotfix.GameSystems.Bag.Core;
using Hotfix.GameSystems.Bag.Data;

namespace Hotfix.GameSystems.Bag.Runtime
{
    /// <summary>
    /// 物品工厂
    /// </summary>
    public static class ItemFactory
    {
        /// <summary>
        /// 创建物品实例
        /// </summary>
        public static ItemData Create(int templateId, int count = 1)
        {
            return ItemData.Create(templateId, count);
        }

        /// <summary>
        /// 创建一组物品（从模板配置）
        /// </summary>
        public static ItemData[] CreateBatch(int[] templateIds, int[] counts = null)
        {
            var items = new System.Collections.Generic.List<ItemData>();
            for (int i = 0; i < templateIds.Length; i++)
            {
                int count = counts != null && i < counts.Length ? counts[i] : 1;
                var item = Create(templateIds[i], count);
                if (item != null)
                    items.Add(item);
            }
            return items.ToArray();
        }

        /// <summary>
        /// 创建奖励物品组（从服务器奖励数据）
        /// </summary>
        public static ItemData[] CreateFromReward(string rewardJson)
        {
            // TODO: 解析奖励JSON，创建对应物品
            return new ItemData[0];
        }

        /// <summary>
        /// 创建测试物品（开发用）
        /// </summary>
        public static ItemData CreateTestItem(int templateId = 10001)
        {
            var item = Create(templateId, 1);
            if (item != null)
            {
                // 设置测试数据
                item.Count = 5;
            }
            return item;
        }

        /// <summary>
        /// 注册所有测试物品模板
        /// </summary>
        public static void RegisterTestTemplates()
        {
            // 测试用物品模板
            RegisterTestItem(10001, "生命药水", ItemType.Consumable, 99, 50);
            RegisterTestItem(10002, "魔法药水", ItemType.Consumable, 99, 50);
            RegisterTestItem(10003, "体力药水", ItemType.Consumable, 20, 100);

            RegisterTestItem(20001, "铁剑", ItemType.Equipment, 1, 500,
                equipmentSlot: EquipmentSlot.Weapon,
                attack: 10, levelRequire: 1);
            RegisterTestItem(20002, "钢甲", ItemType.Equipment, 1, 800,
                equipmentSlot: EquipmentSlot.Chest,
                defense: 15, levelRequire: 3);
            RegisterTestItem(20003, "敏捷戒指", ItemType.Equipment, 1, 1000,
                equipmentSlot: EquipmentSlot.Ring,
                critRate: 0.05f, levelRequire: 5);

            RegisterTestItem(30001, "铁矿石", ItemType.Material, 999, 10);
            RegisterTestItem(30002, "木材", ItemType.Material, 999, 5);
            RegisterTestItem(30003, "魔法水晶", ItemType.Material, 99, 200);

            RegisterTestItem(40001, "任务凭证", ItemType.QuestItem, 1, 0,
                canSell: false, canDrop: false, canTrade: false);
        }

        private static void RegisterTestItem(int id, string name, ItemType type, int maxStack, int price,
            EquipmentSlot equipmentSlot = EquipmentSlot.None,
            int attack = 0, int defense = 0, float critRate = 0,
            int levelRequire = 1,
            bool canSell = true, bool canDrop = true, bool canTrade = true)
        {
            var template = new ItemTemplate
            {
                Id = id,
                Name = name,
                Description = $"测试物品: {name}",
                Type = type,
                Quality = ItemQuality.White,
                MaxStack = maxStack,
                Price = price,
                Icon = $"Icons/{name}",
                CanSell = canSell,
                CanDrop = canDrop,
                CanTrade = canTrade,
                CanDestroy = true,
                EquipSlot = equipmentSlot,
                LevelRequire = levelRequire,
                BaseAttack = attack,
                BaseDefense = defense,
                BaseCritRate = critRate,
            };
            ItemTemplateRegistry.Register(template);
        }
    }
}
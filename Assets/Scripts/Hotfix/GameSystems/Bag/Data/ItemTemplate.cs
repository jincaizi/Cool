using System.Collections.Generic;

namespace Hotfix.GameSystems.Bag.Data
{
    /// <summary>
    /// 物品模板数据（静态配置，配置表生成）
    /// </summary>
    public class ItemTemplate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ItemType Type { get; set; }
        public ItemQuality Quality { get; set; }
        public int MaxStack { get; set; }           // 最大堆叠数
        public int Price { get; set; }              // 售价
        public string Icon { get; set; }            // 图标资源路径
        public bool CanSell { get; set; }           // 可出售
        public bool CanDrop { get; set; }           // 可丢弃
        public bool CanTrade { get; set; }          // 可交易
        public bool CanDestroy { get; set; }        // 可销毁

        // 装备特有属性
        public EquipmentSlot EquipSlot { get; set; }
        public int LevelRequire { get; set; }       // 等级需求
        public int StrengthRequire { get; set; }    // 力量需求
        public int AgilityRequire { get; set; }     // 敏捷需求
        public int IntelligenceRequire { get; set; } // 智力需求

        // 装备属性
        public int BaseHp { get; set; }
        public int BaseMp { get; set; }
        public int BaseAttack { get; set; }
        public int BaseDefense { get; set; }
        public float BaseCritRate { get; set; }
        public float BaseCritDamage { get; set; }

        // 消耗品特有属性
        public int UseEffectId { get; set; }        // 使用效果ID
        public int UseValue { get; set; }           // 使用效果值（如回复100血）

        /// <summary>
        /// 根据ID获取默认模板
        /// </summary>
        public static ItemTemplate GetDefault(int id)
        {
            return new ItemTemplate
            {
                Id = id,
                Name = $"Item_{id}",
                Type = ItemType.Misc,
                Quality = ItemQuality.White,
                MaxStack = 99,
                Price = 10,
                Icon = string.Empty,
                CanSell = true,
                CanDrop = true,
                CanTrade = true,
                CanDestroy = true,
            };
        }
    }

    /// <summary>
    /// 物品模板配置（运行时注册）
    /// </summary>
    public static class ItemTemplateRegistry
    {
        private static readonly Dictionary<int, ItemTemplate> _templates = new();
        private static readonly Dictionary<string, int> _nameToId = new();

        public static void Register(ItemTemplate template)
        {
            if (template == null || template.Id <= 0) return;

            _templates[template.Id] = template;
            if (!string.IsNullOrEmpty(template.Name))
            {
                _nameToId[template.Name] = template.Id;
            }
        }

        public static ItemTemplate Get(int id)
        {
            return _templates.TryGetValue(id, out var template) ? template : null;
        }

        public static ItemTemplate GetByName(string name)
        {
            return _nameToId.TryGetValue(name, out var id) ? Get(id) : null;
        }

        public static bool TryGet(int id, out ItemTemplate template)
        {
            return _templates.TryGetValue(id, out template);
        }

        public static void Clear()
        {
            _templates.Clear();
            _nameToId.Clear();
        }

        public static int Count => _templates.Count;
    }
}

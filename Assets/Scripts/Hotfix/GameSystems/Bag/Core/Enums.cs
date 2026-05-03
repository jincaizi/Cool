namespace Hotfix.GameSystems.Bag.Core
{
    /// <summary>
    /// 物品类型
    /// </summary>
    public enum ItemType
    {
        None = 0,
        Equipment = 1,    // 装备
        Consumable = 2,    // 消耗品
        Material = 3,      // 材料
        QuestItem = 4,    // 任务物品
        Currency = 5,      // 货币
        Misc = 6          // 杂物
    }

    /// <summary>
    /// 物品品质
    /// </summary>
    public enum ItemQuality
    {
        White = 0,   // 普通
        Green = 1,   // 优秀
        Blue = 2,    // 精良
        Purple = 3,  // 史诗
        Orange = 4,  // 传说
    }

    /// <summary>
    /// 装备类型
    /// </summary>
    public enum EquipmentSlot
    {
        None = 0,
        Weapon = 1,      // 武器
        Head = 2,        // 头部
        Chest = 3,       // 胸部
        Legs = 4,        // 腿部
        Boots = 5,       // 靴子
        Gloves = 6,      // 手套
        Ring = 7,        // 戒指
        Necklace = 8,    // 项链
        Cape = 9,        // 披风
    }

    /// <summary>
    /// 背包操作结果
    /// </summary>
    public enum BagResult
    {
        Success = 0,
        Full = 1,           // 背包已满
        InvalidItem = 2,    // 无效物品
        StackFull = 3,      // 堆叠已达上限
        NotEnoughSpace = 4,  // 空间不足（无法拆分）
        Locked = 5,         // 物品已锁定
    }

    /// <summary>
    /// 物品操作类型
    /// </summary>
    public enum ItemOperation
    {
        Add,
        Remove,
        Move,
        Split,
        Merge,
        Use,
        Equip,
        Unequip,
    }
}

namespace Hotfix.GameSystems.Skills.Definition
{
    /// <summary>
    /// 技能类型
    /// </summary>
    public enum SkillType
    {
        BasicAttack,    // 普通攻击
        Special,        // 特殊技能（Q/R）
        Ultimate,       // 大招
        Passive,        // 被动
        Item           // 物品技能
    }

    /// <summary>
    /// 技能品质
    /// </summary>
    public enum SkillQuality
    {
        Common = 1,     // 白色
        Uncommon = 2,   // 绿色
        Rare = 3,       // 蓝色
        Epic = 4,       // 紫色
        Legendary = 5   // 橙色
    }

    /// <summary>
    /// 技能ID枚举 - 定义所有技能ID
    /// </summary>
    public enum SkillID
    {
        None = 0,

        // 普通攻击
        BasicAttack1 = 10001,
        BasicAttack2 = 10002,
        BasicAttack3 = 10003,

        // 特殊技能
        SkillQ = 20001,
        SkillR = 20002,

        // 大招
        Ultimate = 30001,
    }
}
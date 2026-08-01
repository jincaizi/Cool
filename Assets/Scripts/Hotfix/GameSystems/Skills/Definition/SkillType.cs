namespace Hotfix.GameSystems.Skills.Definition
{
    public enum SkillType
    {
        Combo,      // 连击技能 (ComboSkillData)
        Instant,    // 瞬发技能 (InstantSkillData)
        Charged,    // 蓄力技能 (ChargedSkillData)
        Channeled,  // 引导技能 (ChanneledSkillData)
        Projectile, // 投射物技能 (ProjectileSkillData)
        Ultimate,   // 大招
        Passive,    // 被动
        Item,       // 物品技能
        Spin        // 旋转技能 (SpinSkillData)
    }

    public enum SkillQuality
    {
        Common = 1,
        Uncommon = 2,
        Rare = 3,
        Epic = 4,
        Legendary = 5
    }

    public enum SkillID
    {
        None = 0,
        LightAttack = 10001,
        HeavyAttack = 10002,
        BasicAttack3 = 10003,
        SkillQ = 20001,
        SkillR = 20002,
        Ultimate = 30001,
    }
}
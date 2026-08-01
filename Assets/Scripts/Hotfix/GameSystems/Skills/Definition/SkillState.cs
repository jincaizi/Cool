namespace Hotfix.GameSystems.Skills.Definition
{
    /// <summary>
    /// 技能执行子状态 - 用于攻击层内部状态机
    /// </summary>
    public enum SkillSubState
    {
        None = 0,

        // 前置阶段
        Cooldown,       // 冷却中（可触发，但不能释放）
        Ready,          // 就绪（可释放）
        InputBuffer,    // 输入缓冲等待

        // 施法阶段
        Casting,        // 读条中（不可移动）
        Channeling,     // 引导中（可移动）
        Charging,       // 蓄力中（按压蓄力，松发）
        Spinning,       // 持续旋转中

        // 执行阶段
        Execution,      // 释放/执行中（判定帧）
        HitConfirm,     // 命中确认

        // 收尾阶段
        Recovery,       // 收招硬直
        Cancelled,      // 被打断
        Completed       // 正常完成
    }

    /// <summary>
    /// 技能释放类型
    /// </summary>
    public enum ReleaseType
    {
        Instant,        // 瞬发
        Channeled,      // 引导型
        Charged,        // 蓄力型
        Timed           // 读条型
    }

    /// <summary>
    /// 打断来源
    /// </summary>
    public enum InterruptionSource
    {
        None = 0,
        MovementInput,      // 移动输入
        BasicAttack,        // 普攻输入
        AnotherSkill,       // 其他技能
        DamageTaken,        // 受到伤害
        Stun,               // 硬控（眩晕等）
        RollDodge,          // 翻滚
        Parry,              // 招架
        TimeOut             // 超时
    }
}
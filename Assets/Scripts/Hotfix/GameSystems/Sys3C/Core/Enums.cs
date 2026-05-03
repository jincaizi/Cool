namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// FSM 层类型
    /// </summary>
    public enum LayerType
    {
        Base,
        Attack,
        Hit
    }

    /// <summary>
    /// 跳跃阶段
    /// </summary>
    public enum JumpPhase
    {
        Start,
        Air,
        End
    }

    /// <summary>
    /// 打断源
    /// </summary>
    public enum InterruptionSource
    {
        None,
        Damage,
        Stun,
        Knockback,
        Skill,
        Movement
    }

    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
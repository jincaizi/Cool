namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>表现层读取玩家 HP 的最小接口（便于测试注入）。</summary>
    public interface IPlayerHpSource
    {
        float HpPercent { get; }
        bool IsDead { get; }
    }
}

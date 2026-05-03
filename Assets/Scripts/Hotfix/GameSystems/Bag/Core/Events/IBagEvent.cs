namespace Hotfix.GameSystems.Bag.Core.Events
{
    /// <summary>
    /// 背包事件基类
    /// </summary>
    public interface IBagEvent
    {
        // 默认实现
        string EventName => GetType().Name;
        long Timestamp => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
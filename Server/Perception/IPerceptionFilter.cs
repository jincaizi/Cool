namespace KcpServer
{
    /// <summary>
    /// 兴趣过滤接口（spec §5-P1 占位；P3 由 AoiGrid 实现替换，协议不动）
    /// </summary>
    public interface IPerceptionFilter
    {
        /// <summary>返回观察者可见的玩家 ID 集合（不含自身）</summary>
        long[] GetVisiblePlayerIds(long observerPlayerId, System.Collections.Generic.IReadOnlyCollection<PlayerState> allPlayers);
    }
}

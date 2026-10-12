namespace KcpServer
{
    /// <summary>
    /// 服务器墙钟的唯一出口。**快照与施法广播的时间戳必须同基准** —— 客户端拿广播时间戳
    /// 去和快照插值用的渲染时钟比较（spec §6），任一处改用 tick 序号之类的相对量都会
    /// 静默破坏跨客户端一致性。集中到此方法后，「同基准」不再是口头契约。
    /// </summary>
    public static class ServerClock
    {
        public static long NowTicks() => System.DateTime.UtcNow.Ticks;
    }
}

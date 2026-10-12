namespace KcpServer
{
    /// <summary>
    /// **快照 / 施法广播 / 时钟同步**三条路径的服务器墙钟出口。这三处的时间戳必须同基准 ——
    /// 客户端拿广播时间戳去和快照插值用的渲染时钟比较（spec §6）：ClockSyncResponse 收敛出
    /// 偏移，WorldSnapshot.ServerTimestamp 是该偏移下的被比较对象，CastBroadcast / DamageResult
    /// 与之配套。任一处改用 tick 序号之类的相对量都会静默破坏跨客户端一致性。
    ///
    /// 因此改 NowTicks() 的实现等于**改时间基准**，会同时波及以上三条路径。
    ///
    /// 其余 handler 的响应时间戳（LoginHandler / EchoHandler / PositionSyncHandler）
    /// **不在本契约内，故未收敛** —— 客户端不拿它们与渲染时钟比较（只是回显/确认用的信息字段），
    /// 它们与上述三条路径分叉不会影响插值。若将来某处也要参与插值基准，必须改道到此出口，
    /// 而不是就地直写 DateTime.UtcNow.Ticks。
    /// </summary>
    public static class ServerClock
    {
        public static long NowTicks() => System.DateTime.UtcNow.Ticks;
    }
}

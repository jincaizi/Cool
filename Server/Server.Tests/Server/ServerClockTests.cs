using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class ServerClockTests
{
    /// <summary>抓的是「基准」而不是具体值：必须落在 DateTime.UtcNow 的秒级邻域内。
    /// 谁把它改成 tick 序号之类的相对量，这条立刻红。</summary>
    [Fact]
    public void Wall_Clock_Is_On_The_UtcNow_Base()
    {
        long before = System.DateTime.UtcNow.Ticks;
        long now = ServerClock.NowTicks();
        long after = System.DateTime.UtcNow.Ticks;

        Assert.InRange(now, before, after);
    }
}

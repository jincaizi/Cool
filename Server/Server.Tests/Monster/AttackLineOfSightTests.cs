using KcpServer;
using KcpServer.Monster;
using KcpServer.Navigation;
using KcpServer.Tests.Navigation;
using Xunit;

namespace KcpServer.Tests.Monster;

public class AttackLineOfSightTests
{
    /// <summary>2×2 空洞在 x∈[8,10)、z∈[8,10)；玩家站在东边缘外 1m → 点 clearance≈1.0。</summary>
    private static NavigationService HoleNav()
        => new NavigationService(
            NavMeshFixture.Grid(20, 20, (x, z) => !(x >= 8 && x < 10 && z >= 8 && z < 10)),
            "test");

    [Fact]
    public void Low_Clearance_Target_Is_Visible_With_Probe_Radius()
    {
        var nav = HoleNav();
        var from = new Vector3(13.4f, 0f, 9f);
        var to = new Vector3(11f, 0f, 9f);

        // 旧口径：整条线要容得下 1.74m 身体 → 失败
        Assert.False(nav.HasLineOfSight(from, to, 1.74f));
        // 新口径：0.25m 武器探针 → 通过
        Assert.True(AttackLineOfSight.HasLineOfSight(nav, from, to));
    }

    [Fact]
    public void Wall_Still_Blocks_Attack_Line_Of_Sight()
    {
        var nav = new NavigationService(
            NavMeshFixture.Grid(12, 12, (x, z) => !(z == 5 && x < 10)), "test");

        Assert.False(AttackLineOfSight.HasLineOfSight(nav,
            new Vector3(5f, 0f, 3.4f), new Vector3(5f, 0f, 6.4f)));
    }
}

using System.Linq;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class AoiGridTests
{
    private static PlayerState P(long id, float x, float z)
    {
        var p = new PlayerState(id);
        p.UpdatePosition(new Vector3(x, 0, z), 0f, 0f, 1L, 0U);
        return p;
    }

    [Fact]
    public void Observer_Sees_Entities_Within_80m_Radius()
    {
        var grid = new AoiGrid();
        var observer = P(1, 0, 0);
        var near = P(2, 40, 0);   // 40m
        var far = P(3, 90, 0);    // 90m > 80m 视野

        var visible = grid.GetVisible(observer.PlayerId, new[] { observer, near, far });

        Assert.Equal(new[] { 2L }, visible.Select(e => e.PlayerId).ToArray());
    }

    [Fact]
    public void Observer_Does_Not_See_Self()
    {
        var grid = new AoiGrid();
        var observer = P(1, 0, 0);
        var other = P(2, 10, 0);

        var visible = grid.GetVisible(observer.PlayerId, new[] { observer, other });

        Assert.Equal(new[] { 2L }, visible.Select(e => e.PlayerId).ToArray());
    }

    [Fact]
    public void Hysteresis_Requires_90m_To_Leave_Sight()
    {
        // 先建立可见（80m 内），随后移到 85m（80~90 迟滞带内）——仍可见；
        // 移到 95m（>90）——消失
        var grid = new AoiGrid();
        var observer = P(1, 0, 0);
        var mover = P(2, 50, 0);

        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover })); // 首次 50m 进入

        mover.UpdatePosition(new Vector3(85, 0, 0), 0f, 0f, 2L, 1U);
        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover })); // 85m 迟滞带内仍可见

        mover.UpdatePosition(new Vector3(95, 0, 0), 0f, 0f, 3L, 2U);
        Assert.DoesNotContain(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover })); // >90m 消失
    }

    [Fact]
    public void Entity_Reentering_Within_Hysteresis_Stays_Visible()
    {
        // 先进入视野（70m），随后在迟滞带 80~90m 内往返——不闪烁。
        // 注：首次出现必须 ≤80m（进视野阈值）——85m 首现不可见（迟滞只保护"曾可见"者）
        var grid = new AoiGrid();
        var observer = P(1, 0, 0);
        var mover = P(2, 70, 0);

        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover })); // 进入
        mover.UpdatePosition(new Vector3(85, 0, 0), 0f, 0f, 2L, 1U);
        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover })); // 迟滞带内仍可见
        mover.UpdatePosition(new Vector3(88, 0, 0), 0f, 0f, 3L, 2U);
        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover }));
        mover.UpdatePosition(new Vector3(70, 0, 0), 0f, 0f, 4L, 3U);
        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover }));
    }

    [Fact]
    public void Fresh_Entity_In_Hysteresis_Band_Not_Visible()
    {
        // 无状态实现（恒半径 90）会在此假绿——进视野必须 ≤80m
        var grid = new AoiGrid();
        var observer = P(1, 0, 0);
        var fresh = P(2, 85, 0); // 首现 85m：迟滞带内但从未进过视野

        Assert.DoesNotContain(fresh, grid.GetVisible(observer.PlayerId, new[] { observer, fresh }));
    }

    [Fact]
    public void Entity_Leaving_Sight_Then_Returning_To_Band_Stays_Hidden()
    {
        // 出界(>90)后 seen=false 必须存活——回落到 80~90m 迟滞带不得凭旧状态复活（防闪烁核心）
        var grid = new AoiGrid();
        var observer = P(1, 0, 0);
        var mover = P(2, 70, 0);
        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover })); // 进入

        mover.UpdatePosition(new Vector3(95, 0, 0), 0f, 0f, 2L, 1U); // >90 出界
        Assert.DoesNotContain(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover }));

        mover.UpdatePosition(new Vector3(85, 0, 0), 0f, 0f, 3L, 2U); // 回落迟滞带
        Assert.DoesNotContain(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover }));
    }

    [Fact]
    public void Grid_Filters_Distant_Entities_To_Local_Neighborhood()
    {
        // 200 实体按 100m 网格散布在 1km²——观察者 (0,0) 视野 80m 内无实体 → 空。
        // （网格是过滤器非索引：每 observer 仍 O(N) 遍历——数百实体规模下毫秒级，
        //  性能目标仅"不随世界面积增长"，由粗筛保证。）
        var grid = new AoiGrid();
        var players = new System.Collections.Generic.List<PlayerState>();
        var observer = P(1, 0, 0);
        players.Add(observer);
        for (long i = 2; i <= 200; i++)
        {
            float x = (i % 20) * 100f;   // 0..1900m 散布
            float z = (i / 20) * 100f;
            players.Add(P(i, x, z));
        }
        var visible = grid.GetVisible(observer.PlayerId, players);
        Assert.Empty(visible);
    }

    [Fact]
    public void Grid_Cell_Query_Finds_Entity_In_Adjacent_Cell()
    {
        // 观察者在格边缘 (35,0)——39m 的实体在邻格但 <80m 应可见
        var grid = new AoiGrid();
        var observer = P(1, 35, 0);
        var neighbor = P(2, 74, 0); // 距 observer 39m，邻格

        var visible = grid.GetVisible(observer.PlayerId, new[] { observer, neighbor });

        Assert.Equal(new[] { 2L }, visible.Select(e => e.PlayerId).ToArray());
    }

    [Fact]
    public void Sight_Covered_On_Short_Reach_Side_When_Observer_Off_Cell_Center()
    {
        // 覆盖盲区回归（plan 终审 C2 + R2 修正）：观察者 (20,20) 时旧实现"所在格 ±1"
        // 只覆盖短边 60m——65m 内实体不可见。实体须落在 (60,80] 区间才具区分力
        // （55m 在旧代码下也可见——假绿）；新实现 ±LeaveRadius(90) 推导覆盖全半径。
        var grid = new AoiGrid();
        var observer = P(1, 20, 20);
        var west = P(2, -45, 20);  // 65m 西侧（旧代码 col -2 < -1 被漏）
        var south = P(3, 20, -45); // 65m 南侧

        var visible = grid.GetVisible(observer.PlayerId, new[] { observer, west, south });

        Assert.Equal(new[] { 2L, 3L }, visible.Select(e => e.PlayerId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public void Hysteresis_Annulus_Covered_When_Observer_Off_Cell_Center()
    {
        // 复核 R1 回归：观察者 (35,0)（x mod 40 = 35——±80 范围下 +x 覆盖只到 85m）
        // 实体先在 75m 进入视野，随后到 88m（迟滞环内）——必须仍可见并持续被评估。
        // 若候选范围用 ±SightRadius(80)：88m 实体 col 2 > colMax(CellOf(35+80)=2)？
        //   CellOf(115)=2 → colMax=2，88m 实体 col = CellOf(35+88=123) = 3 > 2 → 被漏（bug）；
        //   ±LeaveRadius(90)：CellOf(125)=3 → 包含 → 可见 ✓
        var grid = new AoiGrid();
        var observer = P(1, 35, 0);
        var mover = P(2, 110, 0); // 世界坐标 = 35 + 75（距观察者 75m 进入）

        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover })); // 进入
        mover.UpdatePosition(new Vector3(123, 0, 0), 0f, 0f, 2L, 1U);
        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover })); // 迟滞环 88m 仍可见

        mover.UpdatePosition(new Vector3(130, 0, 0), 0f, 0f, 3L, 2U);
        Assert.DoesNotContain(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover })); // >90 出界
    }

    [Fact]
    public void RemoveEntity_Clears_Observer_Sight_State()
    {
        var grid = new AoiGrid();
        var observer = P(1, 0, 0);
        var mover = P(2, 50, 0);
        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, new[] { observer, mover }));

        // 注销：实体离场（位置不可能 → 从状态清除）后，同 id 重新登录到场——
        // 若旧状态残留（曾可见）会错误地享受迟滞出界豁免
        grid.RemoveEntity(2);
        var reborn = P(2, 85, 0); // 85m：迟滞带内首现——RemoveEntity 清除后不得凭旧状态可见；未清除会假绿
        Assert.DoesNotContain(reborn, grid.GetVisible(observer.PlayerId, new[] { observer, reborn }));
    }
}

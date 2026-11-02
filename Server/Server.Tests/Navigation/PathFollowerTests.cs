using KcpServer;
using KcpServer.Config;
using KcpServer.Monster;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class PathFollowerTests
{
    // npcId 默认取 15 的倍数：重寻相位 = npcId % 15 = 0，周期重寻正好落在 tick 15/30...
    private static (MonsterState Monster, PathFollower Follower) Setup(NavigationService? nav, long npcId = 15)
    {
        var config = new MonsterData
        {
            templateId = 1,
            name = "Slime",
            hp = 100f,
            moveSpeed = 2f,
            bodyRadius = 0.5f,
            detectionRadius = 8f,
            visionAngle = 120f,
            attackRange = 1.5f,
            patrolRadius = 5f
        };
        var monster = new MonsterState(npcId, 1, config, 50001)
        {
            Position = new Vector3(1f, 0f, 1f),
            SpawnPosition = new Vector3(1f, 0f, 1f),
            PatrolCenter = new Vector3(1f, 0f, 1f)
        };
        return (monster, new PathFollower(nav));
    }

    [Fact]
    public void No_Navigation_Falls_Back_To_Direct_Move()
    {
        var (monster, follower) = Setup(null);
        var mover = new SimpleMoveSystem();
        follower.Tick(monster, new Vector3(5f, 0f, 1f), mover, 0.066f, 0);
        Assert.True(monster.Position.X > 1f);
    }

    [Fact]
    public void No_Navigation_Passes_Through_Los_And_Projection()
    {
        var (_, follower) = Setup(null);
        Assert.True(follower.HasLineOfSight(new Vector3(1f, 0f, 1f), new Vector3(9f, 0f, 9f), 0.5f));

        var point = new Vector3(3.5f, 0f, -2f);
        Assert.False(follower.TryProject(point, out Vector3 projected));
        Assert.Equal(point.X, projected.X);
        Assert.Equal(point.Y, projected.Y);
        Assert.Equal(point.Z, projected.Z);
    }

    [Fact]
    public void Follows_Path_And_Reaches_Goal()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);
        for (uint t = 0; t < 300; t++)
        {
            nav.BeginTick(t);
            follower.Tick(monster, goal, mover, 0.066f, t);
        }
        float dx = monster.Position.X - goal.X, dz = monster.Position.Z - goal.Z;
        Assert.True(dx * dx + dz * dz < 1.0f, $"dist={System.MathF.Sqrt(dx * dx + dz * dz)}");
    }

    [Fact]
    public void Off_Path_Deviation_Forces_Repath()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(20, 20, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(10f, 0f, 10f);
        follower.Tick(monster, goal, mover, 0.066f, 0);
        int before = nav.QueryCount;

        monster.Position = new Vector3(6f, 0f, 1f); // 远离 (1,1)→(10,10) 的路径 >1.5m
        follower.Tick(monster, goal, mover, 0.066f, 1);
        follower.Tick(monster, goal, mover, 0.066f, 2);
        Assert.True(nav.QueryCount > before, "off-path monster should request a new path");
    }

    [Fact]
    public void Budget_Rejection_Retries_Next_Tick()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test")
        {
            MaxQueriesPerTick = 0
        };
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        follower.Tick(monster, new Vector3(8f, 0f, 8f), mover, 0.066f, 0);
        Assert.Equal(0, nav.QueryCount);

        nav.MaxQueriesPerTick = 1;
        follower.Tick(monster, new Vector3(8f, 0f, 8f), mover, 0.066f, 1);
        Assert.Equal(1, nav.QueryCount);
    }

    [Fact]
    public void Repath_Is_Throttled_Within_Interval()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav); // npcId 15 → 相位 0，首次周期重寻在 tick 15
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);
        follower.Tick(monster, goal, mover, 0.066f, 0);
        int afterFirst = nav.QueryCount;
        for (uint t = 1; t < 15; t++) follower.Tick(monster, goal, mover, 0.066f, t);
        Assert.Equal(afterFirst, nav.QueryCount);

        follower.Tick(monster, goal, mover, 0.066f, 15);
        Assert.Equal(afterFirst + 1, nav.QueryCount);
    }

    [Fact]
    public void Goal_Move_Forces_Repath_Before_Interval()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        follower.Tick(monster, new Vector3(8f, 0f, 8f), mover, 0.066f, 0);
        int afterFirst = nav.QueryCount;

        // 周期重寻要到 tick 15，但目标移动 >2m 必须立即触发重寻。
        follower.Tick(monster, new Vector3(8f, 0f, 2f), mover, 0.066f, 5);
        Assert.True(nav.QueryCount > afterFirst, "moved goal should repath before the 15-tick interval");
    }

    [Fact]
    public void Repath_Phase_Is_Staggered_By_NpcId()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monsterA, followerA) = Setup(nav, 1); // 相位 1 → tick 16
        var (monsterB, followerB) = Setup(nav, 2); // 相位 2 → tick 17
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);

        nav.BeginTick(0);
        followerA.Tick(monsterA, goal, mover, 0.066f, 0);
        followerB.Tick(monsterB, goal, mover, 0.066f, 0);
        int afterInitial = nav.QueryCount;

        for (uint t = 1; t < 16; t++)
        {
            nav.BeginTick(t);
            followerA.Tick(monsterA, goal, mover, 0.066f, t);
            followerB.Tick(monsterB, goal, mover, 0.066f, t);
        }
        Assert.Equal(afterInitial, nav.QueryCount);

        nav.BeginTick(16);
        followerA.Tick(monsterA, goal, mover, 0.066f, 16);
        followerB.Tick(monsterB, goal, mover, 0.066f, 16);
        Assert.Equal(afterInitial + 1, nav.QueryCount); // 只有相位 1 的 A 重寻

        nav.BeginTick(17);
        followerA.Tick(monsterA, goal, mover, 0.066f, 17);
        followerB.Tick(monsterB, goal, mover, 0.066f, 17);
        Assert.Equal(afterInitial + 2, nav.QueryCount); // 相位 2 的 B 在下一 tick 重寻
    }

    [Fact]
    public void Blocked_Path_Backs_Off_30_Ticks()
    {
        // x == 6 整列不可走：起点（左）与目标（右）分属两个连通分量 → "blocked"。
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (x, _) => x != 6), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);

        follower.Tick(monster, goal, mover, 0.066f, 0);
        Assert.Equal(1, nav.QueryCount);

        for (uint t = 1; t < 30; t++)
        {
            nav.BeginTick(t);
            follower.Tick(monster, goal, mover, 0.066f, t);
        }
        Assert.Equal(1, nav.QueryCount);

        nav.BeginTick(30);
        follower.Tick(monster, goal, mover, 0.066f, 30);
        Assert.Equal(2, nav.QueryCount);
    }

    [Fact]
    public void Stuck_For_Three_Windows_Forces_Repath()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        monster.Config.moveSpeed = 0f; // 每 tick 位移为 0 → 卡死窗口累积
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);

        nav.BeginTick(0);
        follower.Tick(monster, goal, mover, 0.066f, 0);
        Assert.Equal(1, nav.QueryCount);
        Assert.True(follower.HasPath);

        // 预算耗尽：周期重寻被拒绝但保留旧路径，卡死窗口才能连续累积到 3。
        nav.MaxQueriesPerTick = 0;
        for (uint t = 1; t <= 30; t++)
        {
            nav.BeginTick(t);
            follower.Tick(monster, goal, mover, 0.066f, t);
        }
        Assert.Equal(1, nav.QueryCount);
        Assert.False(follower.HasPath); // tick 30 第三次卡死窗口清掉路径

        nav.MaxQueriesPerTick = 1;
        nav.BeginTick(31);
        follower.Tick(monster, goal, mover, 0.066f, 31);
        Assert.Equal(2, nav.QueryCount); // 下 tick 立即重寻
    }
}

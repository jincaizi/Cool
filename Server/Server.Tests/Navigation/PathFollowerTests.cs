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
        var monster = new MonsterState(npcId, 1, config)
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
        Assert.True(follower.Tick(monster, new Vector3(5f, 0f, 1f), mover, 0.066f, 0));
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

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)System.Math.Sqrt(dx * dx + dz * dz);
    }

    [Fact]
    public void Blocked_Path_Stops_Instead_Of_Falling_Back_To_Direct_Move()
    {
        // x == 6 整列不可走：左右分量断开。有导航时失败必须停住，不能朝目标直线穿墙。
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (x, _) => x != 6), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        Vector3 start = monster.Position;

        for (uint t = 0; t < 60; t++)
        {
            nav.BeginTick(t);
            Assert.False(follower.Tick(monster, new Vector3(8f, 0f, 8f), mover, 0.066f, t));
        }
        Assert.True(DistanceXZ(monster.Position, start) < 0.01f,
            $"blocked monster must stay put, moved to ({monster.Position.X:F2},{monster.Position.Z:F2})");
    }

    [Fact]
    public void Budget_Rejection_With_No_Path_Stops_Instead_Of_Moving()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test")
        {
            MaxQueriesPerTick = 0
        };
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        Vector3 start = monster.Position;
        for (uint t = 0; t < 30; t++)
        {
            nav.BeginTick(t);
            Assert.False(follower.Tick(monster, new Vector3(8f, 0f, 8f), mover, 0.066f, t));
        }
        Assert.True(DistanceXZ(monster.Position, start) < 0.01f, "budget-rejected monster must not move");
    }

    [Fact]
    public void Off_Navmesh_Goal_Stops_Instead_Of_Moving()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        Vector3 start = monster.Position;
        for (uint t = 0; t < 30; t++)
        {
            nav.BeginTick(t);
            Assert.False(follower.Tick(monster, new Vector3(-10f, 0f, 6f), mover, 0.066f, t));
        }
        Assert.True(DistanceXZ(monster.Position, start) < 0.01f, "off-navmesh goal must not produce straight-line movement");
    }

    [Fact]
    public void Completed_Path_Does_Not_Continue_To_Off_Mesh_Goal()
    {
        // 目标在网格外 1m（投影半径 2m 内）：路径终点是投影点，走完后不得再直线走向真实目标。
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(-1f, 0f, 6f);
        for (uint t = 0; t < 200; t++)
        {
            nav.BeginTick(t);
            follower.Tick(monster, goal, mover, 0.066f, t);
            Assert.True(monster.Position.X >= -0.05f,
                $"t={t} monster left navmesh toward off-mesh goal at x={monster.Position.X:F2}");
        }
    }

    [Fact]
    public void Goal_Move_Repath_Is_Throttled_By_Its_Own_Interval()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav); // npcId 15 → 相位 0
        var mover = new SimpleMoveSystem();
        nav.BeginTick(0);
        follower.Tick(monster, new Vector3(8f, 0f, 8f), mover, 0.066f, 0);
        int afterInitial = nav.QueryCount;

        // 初始成功把 goalMoved 的最小间隔设到 tick 5。tick 1..4 目标反复跳 6m 也不许查。
        for (uint t = 1; t <= 4; t++)
        {
            nav.BeginTick(t);
            follower.Tick(monster, new Vector3(8f, 0f, 1f), mover, 0.066f, t);
        }
        Assert.Equal(afterInitial, nav.QueryCount);

        // tick 5：间隔已过，允许一次 goalMoved 重寻（目标 (8,9) 已接受）。
        nav.BeginTick(5);
        follower.Tick(monster, new Vector3(8f, 0f, 1f), mover, 0.066f, 5);
        Assert.Equal(afterInitial + 1, nav.QueryCount);

        // tick 6..9：目标继续跳，但下一次 goalMoved 窗口要到 tick 10。
        nav.BeginTick(6);
        follower.Tick(monster, new Vector3(8f, 0f, 9f), mover, 0.066f, 6);
        nav.BeginTick(7);
        follower.Tick(monster, new Vector3(8f, 0f, 1f), mover, 0.066f, 7);
        nav.BeginTick(8);
        follower.Tick(monster, new Vector3(8f, 0f, 9f), mover, 0.066f, 8);
        nav.BeginTick(9);
        follower.Tick(monster, new Vector3(8f, 0f, 1f), mover, 0.066f, 9);
        Assert.Equal(afterInitial + 1, nav.QueryCount);

        nav.BeginTick(10);
        follower.Tick(monster, new Vector3(8f, 0f, 9f), mover, 0.066f, 10);
        Assert.Equal(afterInitial + 2, nav.QueryCount); // 间隔已过，允许再次 goalMoved 重寻
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

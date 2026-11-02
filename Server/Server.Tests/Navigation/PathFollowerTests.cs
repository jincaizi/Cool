using KcpServer;
using KcpServer.Config;
using KcpServer.Monster;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class PathFollowerTests
{
    private static (MonsterState Monster, PathFollower Follower) Setup(NavigationService? nav)
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
        var monster = new MonsterState(1, 1, config, 50001)
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
    public void Follows_Path_And_Reaches_Goal()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);
        for (uint t = 0; t < 300; t++) follower.Tick(monster, goal, mover, 0.066f, t);
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
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);
        follower.Tick(monster, goal, mover, 0.066f, 0);
        int afterFirst = nav.QueryCount;
        for (uint t = 1; t < 15; t++) follower.Tick(monster, goal, mover, 0.066f, t);
        Assert.Equal(afterFirst, nav.QueryCount);
    }
}

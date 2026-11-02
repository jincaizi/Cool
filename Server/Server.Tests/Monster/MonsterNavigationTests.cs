using KcpServer;
using KcpServer.Monster;
using KcpServer.Navigation;
using KcpServer.Tests.Navigation;
using Xunit;

namespace KcpServer.Tests.Monster;

public class MonsterNavigationTests
{
    [Fact]
    public void Wolf_Chases_Player_Around_Wall_Without_Entering_Blocked_Cells()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, z) => !(z == 5 && x < 10));
        var nav = new NavigationService(graph, "test");

        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long wolfId = module.SpawnMonster(2, new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f));
        var wolf = store.GetPlayer(wolfId)!;
        wolf.Rotation = 0f; // 面向 +Z

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(1f, 0f, 9f), 0f, 0f, 1L, 0U);

        for (uint t = 0; t < 400; t++)
        {
            module.Tick(t);
            // 不允许穿过 z=5 的墙
            Assert.False(wolf.Position.Z >= 5f && wolf.Position.Z < 6f && wolf.Position.X < 10f,
                $"t={t} wolf entered wall at ({wolf.Position.X:F2},{wolf.Position.Z:F2})");
        }

        float dx = wolf.Position.X - player.Position.X;
        float dz = wolf.Position.Z - player.Position.Z;
        Assert.True(dx * dx + dz * dz <= 2.5f * 2.5f,
            $"wolf should reach attack range, pos=({wolf.Position.X:F2},{wolf.Position.Z:F2})");
    }

    [Fact]
    public void Target_Is_Kept_When_Out_Of_Vision_But_Within_Drop_Radius()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(30, 30, (_, _) => true);
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long wolfId = module.SpawnMonster(2, new Vector3(10f, 0f, 10f), new Vector3(10f, 0f, 10f));
        var wolf = store.GetPlayer(wolfId)!;
        wolf.Rotation = 0f;

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(10f, 0f, 20f), 0f, 0f, 1L, 0U);
        for (uint t = 0; t < 5; t++) module.Tick(t);
        Assert.Equal(1L, module.GetState(wolfId)!.TargetId);

        // 目标走到怪物背后（视野锥外），但仍在 drop radius 内
        player.UpdatePosition(new Vector3(10f, 0f, 0f), 0f, 0f, 2L, 0U);
        for (uint t = 5; t < 10; t++) module.Tick(t);
        Assert.Equal(1L, module.GetState(wolfId)!.TargetId);
    }

    [Fact]
    public void Monster_Does_Not_Attack_Through_Wall()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, z) => !(z == 5 && x < 10));
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long wolfId = module.SpawnMonster(2, new Vector3(1f, 0f, 4.5f), new Vector3(1f, 0f, 4.5f));
        var wolf = store.GetPlayer(wolfId)!;
        wolf.Rotation = 0f;

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(1f, 0f, 6.4f), 0f, 0f, 1L, 0U);

        Vector3 start = wolf.Position;
        for (uint t = 0; t < 60; t++)
        {
            module.Tick(t);
            Assert.Empty(module.DrainPendingCasts());
        }

        // 攻击距离内但隔墙：不能停步冻结（旧实现停在起点、却继续广播上一 tick 的 Run）——
        // 必须继续寻路绕行。绕路约 20m，60 tick（约 14m）到不了，故不会误命中。
        float dx = wolf.Position.X - start.X;
        float dz = wolf.Position.Z - start.Z;
        Assert.True(dx * dx + dz * dz > 1f,
            $"wolf should path around the wall, moved=({wolf.Position.X:F2},{wolf.Position.Z:F2})");
    }

    [Fact]
    public void Spawn_Off_Navmesh_Throws()
    {
        // 4x4 大洞的中心距最近 navmesh 2m，超过 0.5m 投影容差
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, z) => !(x >= 4 && x < 8 && z >= 4 && z < 8));
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);

        Assert.Throws<System.ArgumentException>(() =>
            module.SpawnMonster(1, new Vector3(6f, 0f, 6f), new Vector3(6f, 0f, 6f)));
    }

    [Fact]
    public void Unreachable_Target_Does_Not_Walk_Through_Blocked_Column()
    {
        // x == 6 整列不可走（无缝缺口）：左右两个连通分量。怪物不能直线穿过去。
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, _) => x != 6);
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long wolfId = module.SpawnMonster(2, new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f));
        var wolf = store.GetPlayer(wolfId)!;
        wolf.Rotation = 0f; // 面向 +Z；玩家在东北方向，仍在 120° 视锥内

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(9f, 0f, 9f), 0f, 0f, 1L, 0U);

        for (uint t = 0; t < 120; t++)
        {
            module.Tick(t);
            Assert.True(wolf.Position.X < 6.01f,
                $"t={t} wolf crossed the blocked column at ({wolf.Position.X:F2},{wolf.Position.Z:F2})");
            Assert.True(graph.IsPointNavigable(wolf.Position, 0.5f),
                $"t={t} wolf left the navmesh at ({wolf.Position.X:F2},{wolf.Position.Z:F2})");
        }
        var state = module.GetState(wolfId)!;
        Assert.False(state.Moving, "blocked chase must report Moving=false");
        Assert.Equal(0f, wolf.Speed, 0.0001f); // 快照速度按实际位移
    }

    [Fact]
    public void Budget_Zero_Keeps_Monster_Still()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(20, 20, (_, _) => true);
        var nav = new NavigationService(graph, "test") { MaxQueriesPerTick = 0 };
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long wolfId = module.SpawnMonster(2, new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f));
        var wolf = store.GetPlayer(wolfId)!;
        wolf.Rotation = 0f;

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(1f, 0f, 10f), 0f, 0f, 1L, 0U);

        for (uint t = 0; t < 60; t++) module.Tick(t);

        float dx = wolf.Position.X - 1f, dz = wolf.Position.Z - 1f;
        Assert.True(dx * dx + dz * dz < 0.01f, $"budget-rejected monster moved to ({wolf.Position.X:F2},{wolf.Position.Z:F2})");
        Assert.False(module.GetState(wolfId)!.Moving);
        Assert.Equal(0f, wolf.Speed, 0.0001f);
    }

    [Fact]
    public void Off_Navmesh_Chase_Target_Keeps_Monster_On_Mesh()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (_, _) => true);
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long wolfId = module.SpawnMonster(2, new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f));
        var wolf = store.GetPlayer(wolfId)!;
        wolf.Rotation = 0f;

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(1f, 0f, 15f), 0f, 0f, 1L, 0U); // 正前方；网格外 3m（>2m 投影半径）

        for (uint t = 0; t < 60; t++) module.Tick(t);

        float dx = wolf.Position.X - 1f, dz = wolf.Position.Z - 1f;
        Assert.True(dx * dx + dz * dz < 0.01f, $"off-navmesh goal made wolf move to ({wolf.Position.X:F2},{wolf.Position.Z:F2})");
        Assert.True(graph.IsPointNavigable(wolf.Position, 0.5f));
    }

    [Fact]
    public void Patrol_Fallback_Uses_Projected_Center_Not_Legacy_Ring()
    {
        // 只有 (0,0)..(2,2) 一小块可走：半径 5m 的四个巡逻候选全部采不到点。
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, z) => x <= 1 && z <= 1);
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long id = module.SpawnMonster(1, new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f));
        MonsterState state = module.GetState(id)!;

        Assert.Single(state.PatrolPoints);
        Assert.True(graph.IsPointNavigable(state.PatrolPoints[0], 0.5f));
        Assert.True(DistanceXZ(state.PatrolPoints[0], state.SpawnPosition) < 0.5f,
            "fallback patrol point must be the projected patrol center");
        Assert.True(DistanceXZ(state.PatrolCenter, state.SpawnPosition) < 0.001f,
            "patrol center must be the projected spawn when navigation is active");
    }

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)System.Math.Sqrt(dx * dx + dz * dz);
    }

    [Fact]
    public void Without_Navigation_Monster_Still_Moves_Legacy()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long id = module.SpawnMonster(1, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
        var entity = store.GetPlayer(id)!;
        Vector3 start = entity.Position;
        for (uint t = 0; t < 60; t++) module.Tick(t);
        Assert.NotEqual(start, entity.Position);
    }
}

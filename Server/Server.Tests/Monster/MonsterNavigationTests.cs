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

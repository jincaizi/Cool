using System;
using System.IO;
using KcpServer;
using KcpServer.Config;
using KcpServer.Monster;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class DemoDayNavBinTests
{
    private static readonly Vector3[] Spawns =
    {
        new(-30f, 0f, 0f),
        new(0f, 0f, -20f),
        new(0f, 0f, -35f),
        new(10f, 0f, -15f),
        new(10f, 0f, 15f),
        new(18.333f, 0f, 18.389f)
    };

    private static string NavBinPath => Path.Combine(AppContext.BaseDirectory, "Config", "nav", "demoday.navbin");

    [Fact]
    public void Shipped_NavBin_Loads_And_Covers_Fixed_Spawns()
    {
        string path = NavBinPath;
        Assert.True(File.Exists(path), $"missing {path} - run the Unity exporter");

        NavData data = NavDataFormat.Read(path);
        Assert.Equal("demoday", data.MapId);
        Assert.True(data.Vertices.Length > 100);
        Assert.NotEmpty(data.Tiles);

        var graph = NavMeshGraph.FromData(data);
        foreach (Vector3 spawn in Spawns)
        {
            Assert.True(graph.FindNearestTriangle(spawn, 1f, out int triangle, out float distance),
                $"spawn {spawn} not on navmesh");
            Assert.True(distance <= 0.5f, $"spawn {spawn} nearest={distance:F2}");
            Assert.True(triangle >= 0);
        }

        // 出生点用点级 clearance（I2）：三角面级取质心/边中点最小值，会高估尖角处的可站立空间。
        Assert.True(graph.IsPointNavigable(Spawns[5], 1.74f),
            $"SpiderBoss spawn point clearance={graph.GetPointClearance(Spawns[5]):F2} < 1.74");
        Assert.True(graph.FindNearestPoint(Spawns[5], 1f, out _, out _, out int bossTriangle));
        Assert.True(graph.GetClearance(bossTriangle) >= 1.74f,
            $"SpiderBoss spawn triangle clearance={graph.GetClearance(bossTriangle):F2} < 1.74");
    }

    [Fact]
    public void Shipped_NavBin_Can_Path_Between_Two_Layout_Points()
    {
        NavData data = NavDataFormat.Read(NavBinPath);
        var finder = new NavMeshPathfinder(NavMeshGraph.FromData(data));

        // 跨岛 [0]/[5] 被 x≈0 河谷分断（blocked）；同岛 [3]->[5] 可通。
        NavPath path = finder.FindPath(Spawns[3], Spawns[5], 0.5f);
        Assert.True(path.Success, path.FailReason);
        Assert.True(path.Length > 0f);
    }

    [Fact]
    public void Shipped_NavBin_Detours_Around_Obstacle_And_Samples_Stay_Navigable()
    {
        NavData data = NavDataFormat.Read(NavBinPath);
        var graph = NavMeshGraph.FromData(data);
        var finder = new NavMeshPathfinder(graph);

        // 已知绕障路径：(-42,-42) -> (-42,-18) 直线 24m，navmesh 上实际 49.8m（绕西侧高地）。
        var start = new Vector3(-42f, 0f, -42f);
        var goal = new Vector3(-42f, 0f, -18f);
        NavPath path = finder.FindPath(start, goal, 0.5f);
        Assert.True(path.Success, path.FailReason);
        float straight = DistanceXZ(start, goal);
        Assert.True(path.Length > straight * 1.5f,
            $"known detour should exceed 1.5x straight: path={path.Length:F2} straight={straight:F2}");

        // 0.25m 采样逐点校验（与 NavMeshPathfinder.ValidationStep 一致；端点 1m 内按规则放宽）。
        const float bodyRadius = 0.5f;
        float along = 0f;
        int sampled = 0;
        for (int i = 1; i < path.Waypoints.Length; i++)
        {
            Vector3 a = path.Waypoints[i - 1], b = path.Waypoints[i];
            float length = DistanceXZ(a, b);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / 0.25f));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float distance = along + t * length;
                if (distance < 1f || path.Length - distance < 1f) continue;
                var p = new Vector3(a.X + (b.X - a.X) * t, a.Y, a.Z + (b.Z - a.Z) * t);
                Assert.True(graph.IsPointNavigable(p, bodyRadius),
                    $"point ({p.X:F2},{p.Z:F2}) at {distance:F2}m lacks clearance for r={bodyRadius}");
                sampled++;
            }
            along += length;
        }
        Assert.True(sampled > 20, $"only {sampled} sampled points");
    }

    [Fact]
    public void Shipped_NavBin_SpiderBoss_Reachable_And_Unreachable()
    {
        NavData data = NavDataFormat.Read(NavBinPath);
        var graph = NavMeshGraph.FromData(data);
        var finder = new NavMeshPathfinder(graph);
        Vector3 boss = Spawns[5];
        Assert.True(graph.IsPointNavigable(boss, 1.74f));

        // 同岛代表性可达：Boss 出生点 -> (18,6)，1.74m 半径。
        var reachable = new Vector3(18f, 0f, 6f);
        NavPath ok = finder.FindPath(boss, reachable, 1.74f);
        Assert.True(ok.Success, ok.FailReason);
        Assert.True(ok.Length > DistanceXZ(boss, reachable),
            $"path {ok.Length:F2} should be longer than straight {DistanceXZ(boss, reachable):F2}");
        foreach (Vector3 waypoint in ok.Waypoints)
            Assert.True(graph.IsPointNavigable(waypoint, 1.74f),
                $"Boss waypoint ({waypoint.X:F2},{waypoint.Z:F2}) lacks 1.74 clearance");

        // 代表性不可达：跨岛目标必须 blocked（河谷分断）。
        NavPath blocked = finder.FindPath(boss, Spawns[0], 1.74f);
        Assert.False(blocked.Success);
        Assert.Equal("blocked", blocked.FailReason);
    }

    [Fact]
    public void Shipped_NavBin_SpiderBoss_NavRadius_Keeps_Forest_Routes_Open()
    {
        NavData data = NavDataFormat.Read(NavBinPath);
        var graph = NavMeshGraph.FromData(data);
        var finder = new NavMeshPathfinder(graph);
        MonsterData boss = ConfigLoader.LoadMonsterTemplates(
            Path.Combine(AppContext.BaseDirectory, "Config", "monster_config.json"))[4];

        Assert.Equal(1.74f, boss.bodyRadius, 0.001f);
        Assert.Equal(1.0f, boss.NavigationRadiusOf, 0.001f);

        // 2026-11-07 实测的“卡住现场”：Boss 出生点到这些点的直线都穿过 navmesh 空洞
        // （树/石头），bodyRadius 1.74 时整片区域无路可走，PathFollower 会原地停住。
        var goals = new[]
        {
            new Vector3(23.17f, 0f, 12.50f),
            new Vector3(25.17f, 0f, 6.50f),
            new Vector3(30.17f, 0f, 11.50f),
        };
        foreach (Vector3 goal in goals)
        {
            NavPath blocked = finder.FindPath(Spawns[5], goal, boss.bodyRadius);
            Assert.False(blocked.Success,
                $"bodyRadius={boss.bodyRadius} 在 ({goal.X:F1},{goal.Z:F1}) 应无路可走（bug 现场已变，需重新取样）");

            NavPath path = finder.FindPath(Spawns[5], goal, boss.NavigationRadiusOf);
            Assert.True(path.Success,
                $"navRadius={boss.NavigationRadiusOf} 应能绕行到 ({goal.X:F1},{goal.Z:F1})：{path.FailReason}");
            Assert.True(path.Length > DistanceXZ(Spawns[5], goal),
                "绕行路径应长于直线距离");
        }
    }

    [Fact]
    public void SpiderBoss_Chases_Player_Into_Tree_Cluster_Instead_Of_Freezing()
    {
        NavData data = NavDataFormat.Read(NavBinPath);
        var graph = NavMeshGraph.FromData(data);
        var nav = new NavigationService(graph, "demoday");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long bossId = module.SpawnMonster(4, Spawns[5], Spawns[5]);

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        var goal = new Vector3(23.17f, 0f, 12.50f);
        player.UpdatePosition(goal, 0f, 0f, 1L, 0U);
        module.Taunt(bossId, 1, durationTicks: 2000, tick: 0);

        var boss = store.GetPlayer(bossId)!;
        bool reached = false;
        for (uint t = 0; t < 900; t++)
        {
            nav.BeginTick(t);
            module.Tick(t);
            if (DistanceXZ(boss.Position, goal) <= 2.6f) { reached = true; break; }
        }

        Assert.True(reached,
            $"Boss 应绕行到攻击距离而不是原地卡住，最终位置 " +
            $"({boss.Position.X:F2},{boss.Position.Z:F2})，距目标 {DistanceXZ(boss.Position, goal):F2}");
    }

    [Fact]
    public void SpiderBoss_Blocked_Path_Stops_And_Stays_On_NavMesh()
    {
        NavData data = NavDataFormat.Read(NavBinPath);
        var graph = NavMeshGraph.FromData(data);
        var nav = new NavigationService(graph, "demoday");
        var config = new MonsterData
        {
            templateId = 4,
            name = "SpiderBoss",
            hp = 600f,
            moveSpeed = 2.5f,
            bodyRadius = 1.74f,
            detectionRadius = 25f,
            visionAngle = 120f,
            attackRange = 2.5f,
            patrolRadius = 12f
        };
        var monster = new MonsterState(15, 4, config)
        {
            Position = Spawns[5],
            SpawnPosition = Spawns[5],
            PatrolCenter = Spawns[5]
        };
        var follower = new PathFollower(nav);
        var mover = new SimpleMoveSystem();
        Vector3 start = monster.Position;

        for (uint t = 0; t < 40; t++)
        {
            nav.BeginTick(t);
            Assert.False(follower.Tick(monster, Spawns[0], mover, 0.066f, t));
            Assert.True(graph.IsPointNavigable(monster.Position, 1.74f),
                $"t={t} Boss left navmesh at ({monster.Position.X:F2},{monster.Position.Z:F2})");
        }
        Assert.True(DistanceXZ(monster.Position, start) < 0.01f,
            $"blocked Boss moved to ({monster.Position.X:F2},{monster.Position.Z:F2})");
    }

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}

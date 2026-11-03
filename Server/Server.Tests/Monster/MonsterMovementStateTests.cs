using System;
using KcpServer;
using KcpServer.Config;
using KcpServer.Monster;
using KcpServer.Movement;
using KcpServer.Navigation;
using KcpServer.Tests.Navigation;
using Xunit;

namespace KcpServer.Tests.Monster;

/// <summary>I5：Moving/快照速度必须按**实际位移**，而不是行为树意图。</summary>
public class MonsterMovementStateTests
{
    private static MonsterState MakeMonster(Vector3 position, float moveSpeed = 2f)
    {
        var config = new MonsterData
        {
            templateId = 1,
            name = "Slime",
            hp = 100f,
            moveSpeed = moveSpeed,
            bodyRadius = 0.5f,
            detectionRadius = 8f,
            visionAngle = 120f,
            attackRange = 1.5f,
            patrolRadius = 5f,
            skills = { new MonsterSkillConfig { skillId = 50001, maxRange = 1.5f } }
        };
        return new MonsterState(1, 1, config) { Position = position, SpawnPosition = position };
    }

    [Fact]
    public void MoveTowards_Returns_Actual_Displacement()
    {
        var monster = MakeMonster(new Vector3(0f, 0f, 0f));
        var mover = new SimpleMoveSystem();

        float moved = mover.MoveTowards(monster, new Vector3(1f, 0f, 0f), 0.066f);
        Assert.Equal(0.132f, moved, 0.001f);
        Assert.Equal(moved, DistanceXZ(monster.Position, new Vector3(0f, 0f, 0f)), 0.0001f);

        // 零方向：不移动、速度清零、返回 0
        float stopped = mover.MoveTowards(monster, default, 0.066f);
        Assert.Equal(0f, stopped, 0.0001f);
        Assert.Equal(0f, monster.CurrentVelocity.X, 0.0001f);
        Assert.Equal(0f, monster.CurrentVelocity.Z, 0.0001f);
    }

    [Fact]
    public void Orca_Zero_Solution_Reports_No_Movement()
    {
        // 左右各一个正好贴身的邻居（间距 = 半径和 1.0m，零速度）：ORCA 把 x 方向约束为 0，
        // 期望位移 +X 被解成 0 → MoveTowards 必须返回 0 且位置不动（客户端不能播 Run）。
        var store = new EntityStore();
        var avoidance = new AvoidanceSystem(store, _ => 0.5f);
        store.AddNpc(1, "self", 1, 100f, new Vector3(0f, 0f, 0f));
        store.AddNpc(2, "left", 1, 100f, new Vector3(-1f, 0f, 0f));
        store.AddNpc(3, "right", 1, 100f, new Vector3(1f, 0f, 0f));

        avoidance.EnsureSnapshot(0);
        var monster = MakeMonster(new Vector3(0f, 0f, 0f));
        var mover = new SimpleMoveSystem(avoidance);

        float moved = mover.MoveTowards(monster, new Vector3(1f, 0f, 0f), 0.066f);
        Assert.Equal(0f, moved, 0.0001f);
        Assert.Equal(0f, monster.Position.X, 0.0001f);
        Assert.Equal(0f, monster.Position.Z, 0.0001f);
    }

    [Fact]
    public void Attack_Cooldown_Sets_Moving_False()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long id = module.SpawnMonster(1, new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f));
        var entity = store.GetPlayer(id)!;
        MonsterState state = module.GetState(id)!;
        state.Follower = null; // 无导航直线语义；LOS 门不参与
        state.TargetId = 1L;
        state.LastKnownTargetPosition = state.Position + new Vector3(0f, 0f, 1f);
        state.Moving = true;
        entity.RecordCast(state.PrimarySkillId, 100); // 冷却中

        var ctx = new MonsterTickContext(state, entity, module, new SimpleMoveSystem(), 0.066f, 100);
        BtStatus status = MonsterBtActions.Attack(ctx);

        Assert.Equal(BtStatus.Running, status); // 冷却门：原地等
        Assert.False(state.Moving);
    }

    [Fact]
    public void Attack_Cooldown_Flushes_Zero_Speed()
    {
        var graph = NavMeshFixture.Grid(12, 12, (_, _) => true);
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long id = module.SpawnMonster(2, new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f));
        var entity = store.GetPlayer(id)!;
        entity.Rotation = 0f; // 面向 +Z

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(1f, 0f, 2.5f), 0f, 0f, 1L, 0U); // 攻击距离内

        // 预置冷却：Attack 决策走"冷却门"，本 tick 原地等；模块 flush 必须写 Speed=0。
        entity.RecordCast(module.GetState(id)!.PrimarySkillId, 0);
        module.Tick(0);

        Assert.False(module.GetState(id)!.Moving);
        Assert.Equal(0f, entity.Speed, 0.0001f);
    }

    [Fact]
    public void Published_Config_Respects_Waypoint_Advance_Invariant()
    {
        // PathFollower 推进半径 = bodyRadius*0.5；单 tick 位移必须小于它，否则会跨过路点绕圈。
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Config", "monster_config.json");
        var templates = ConfigLoader.LoadMonsterTemplates(path);
        Assert.NotEmpty(templates);
        foreach (var kv in templates)
        {
            MonsterData config = kv.Value;
            float step = config.moveSpeed * MonsterModule.TickDeltaSeconds;
            Assert.True(step < config.bodyRadius * 0.5f,
                $"template {config.templateId} ({config.name}): moveSpeed*dt={step:F3} >= bodyRadius*0.5={config.bodyRadius * 0.5f:F3}");
        }
    }

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }
}

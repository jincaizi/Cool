using System;
using System.Linq;
using Xunit;

namespace KcpServer.Tests;

public class BotRunnerTests
{
    [Fact]
    public void Spawn_Registers_Entities_With_Next_Ids()
    {
        var store = new EntityStore();
        long next = 900;
        var runner = new BotRunner(store, () => next++); // id 由委托递增提供（服务器用 Interlocked 计数器）
        runner.Spawn(3);

        Assert.Equal(3, store.Count);
        Assert.NotNull(store.GetPlayer(900));
        Assert.NotNull(store.GetPlayer(901));
        Assert.NotNull(store.GetPlayer(902));
    }

    [Fact]
    public void Tick_Moves_Bots_Within_World_Bounds()
    {
        var store = new EntityStore();
        long next = 1;
        var runner = new BotRunner(store, () => next++);
        runner.Spawn(5);
        // spawn 位置随机——记录基线用于"确实移动"判定
        var before = store.All.ToDictionary(p => p.PlayerId, p => (p.Position.X, p.Position.Z));

        for (int i = 0; i < 100; i++) // ~6.6s 模拟
            runner.Tick((uint)i + 1);

        foreach (var p in store.All)
        {
            Assert.InRange(p.Position.X, -BotRunner.WorldHalfExtent, BotRunner.WorldHalfExtent);
            Assert.InRange(p.Position.Z, -BotRunner.WorldHalfExtent, BotRunner.WorldHalfExtent);
        }
        // 每个 bot 都相对初始位置移动了（直线游走，100 tick × 0.53m = 53m 位移——除非撞界反弹仍在动）
        foreach (var p in store.All)
        {
            var (bx, bz) = before[p.PlayerId];
            Assert.True(bx != p.Position.X || bz != p.Position.Z,
                $"bot {p.PlayerId} 未移动（初始 {bx},{bz} → {p.Position.X},{p.Position.Z}）");
        }
    }

    [Fact]
    public void Despawn_Removes_Entities()
    {
        var store = new EntityStore();
        long next = 1;
        var runner = new BotRunner(store, () => next++);
        runner.Spawn(4);
        runner.DespawnAll();

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Bot_Is_Registered_As_PlayerBot()
    {
        var store = new EntityStore();
        long next = 1;
        var bot = new BotRunner(store, () => next++);
        bot.Spawn(3);
        Assert.Equal(3, store.Count); // 防空集空转通过
        foreach (var p in store.All)
            Assert.Equal((byte)EntityKind.PlayerBot, p.Kind);
    }

    [Fact]
    public void Bot_Can_Be_Damaged_And_Respawns()
    {
        var store = new EntityStore();
        long next = 1;
        var bot = new BotRunner(store, () => next++);
        bot.Spawn(1);
        var target = store.All.First();

        target.ApplyDamage(9999f);
        Assert.False(target.IsAlive);

        target.MarkDead(1000);
        Assert.True(target.TryRespawn(1000 + 122, 66)); // 122 tick × 66ms = 8052ms ≥ 8s
        Assert.True(target.IsAlive);
    }

    [Fact]
    public void Default_Spawn_Stays_Bit_Identical_To_The_Pre_Dense_Path()
    {
        var store = new EntityStore();
        long next = 1;
        var runner = new BotRunner(store, () => next++, seed: 12345);
        runner.Spawn(20);

        // 回归护栏：不带 --bots-radius 时撒点必须与"密集模式"改造前逐位一致。默认分支的乘法结合序
        // 不能重排——(t×500)×0.8 与 t×(500×0.8)=t×400 在 30% 的 t 上差 1 ULP（500 万采样实测
        // 1,521,132 处不等），而既有用例只断言范围，抓不到这种漂移。
        var legacy = new Random(12345);
        for (int i = 0; i < 20; i++)
        {
            float legacyX = (float)(legacy.NextDouble() * 2 - 1) * BotRunner.WorldHalfExtent * 0.8f;
            float legacyZ = (float)(legacy.NextDouble() * 2 - 1) * BotRunner.WorldHalfExtent * 0.8f;
            legacy.NextDouble(); // RandomDir 抽轴向
            legacy.NextDouble(); // RandomDir 抽符号
            var p = store.GetPlayer(1 + i)!;
            Assert.True(legacyX == p.Position.X && legacyZ == p.Position.Z,
                $"bot#{1 + i} 撒点漂移：legacy=({legacyX:R},{legacyZ:R}) actual=({p.Position.X:R},{p.Position.Z:R})");
        }
    }

    [Fact]
    public void Dense_Radius_Shrinks_Spawn_And_Bounce_Bounds()
    {
        var store = new EntityStore();
        long nextId = 1;
        var runner = new BotRunner(store, () => nextId++, seed: 12345,
            avoidance: null, denseRadius: 50f);
        runner.Spawn(20);

        foreach (var player in store.All)
        {
            Assert.True(Math.Abs(player.Position.X) <= 50f, "撒点必须收在 ±50（默认是 ±400）");
            Assert.True(Math.Abs(player.Position.Z) <= 50f, "撒点必须收在 ±50（默认是 ±400）");
        }

        // 推 600 tick（≈40s，8m/s 会撞界多次）——反弹边界也必须收到 ±50，否则 bot 会飘出密集区
        for (uint tick = 1; tick <= 600; tick++) runner.Tick(tick);
        foreach (var player in store.All)
        {
            Assert.True(Math.Abs(player.Position.X) <= 50f, "反弹边界必须收到 ±50");
            Assert.True(Math.Abs(player.Position.Z) <= 50f, "反弹边界必须收到 ±50");
        }
    }
}

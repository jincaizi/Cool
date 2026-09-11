using Cool.Protocol;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>P5 T2：怪物模块纯核心（无 Server3C/session 依赖——主循环模型直测）</summary>
public class MonsterModuleTests
{
    private static (EntityStore Store, MonsterModule Module) Create(int count = 4)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        module.SpawnDefaultLayout(count);
        return (store, module);
    }

    private static PlayerState LoginPlayer(EntityStore store, long id, Vector3 position)
    {
        var p = store.AddPlayer(id, $"P{id}");
        p.IsLoggedIn = true;
        p.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return p;
    }

    [Fact]
    public void Spawn_Registers_Npc_Entities_With_Template_And_Hp()
    {
        var (store, module) = Create();

        Assert.Equal(4, module.Count);
        Assert.Equal(4, store.Count);
        foreach (var p in store.All)
        {
            Assert.Equal((byte)EntityKind.Npc, p.Kind);
            Assert.True(p.TemplateId is 1 or 2);
            Assert.True(p.IsAlive);
        }
        // Wolf hp 150 / Slime hp 100（配置实测）
        var wolf = store.GetPlayer(MonsterModule.NpcIdSeed + 1)!;
        Assert.Equal(2, wolf.TemplateId);
        Assert.Equal(150f, wolf.Hp);
    }

    [Fact]
    public void Tick_With_No_Players_Patrols_Monsters()
    {
        var (store, module) = Create();
        var slime = store.GetPlayer(MonsterModule.NpcIdSeed + 2)!; // 布局 [1]：Slime (40,0,0)
        var start = slime.Position;

        for (uint t = 0; t < 60; t++) module.Tick(t);

        Assert.NotEqual(start, slime.Position);                       // 巡逻移动
        float dx = slime.Position.X - 40f, dz = slime.Position.Z - 0f;
        Assert.True(dx * dx + dz * dz <= 5f * 5f + 0.01f);           // 巡逻半径 5 内（D-P5-9 零玩家持续运转）
    }

    [Fact]
    public void Detection_Aggro_And_Attack_Queue_Targets_Closest_Player()
    {
        var (store, module) = Create();
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f)); // 原点——Wolf[0] (12,0,0) 面向原点，检测 15m ≥ 12m
        var wolf = store.GetPlayer(MonsterModule.NpcIdSeed + 1)!;

        for (uint t = 0; t < 200; t++) module.Tick(t); // 3.5m/s × 200×66ms ≈ 46m 行程——足够追到 2m 内

        float dist = Vector3.Distance(wolf.Position, new Vector3(0f, 0f, 0f));
        Assert.True(dist <= 2.5f, $"wolf should chase to attack range, dist={dist}");

        var casts = module.DrainPendingCasts();
        Assert.NotEmpty(casts);
        Assert.Equal(MonsterModule.NpcIdSeed + 1, casts[0].NpcId);
        Assert.Equal(50002, casts[0].Request.SkillId);
        Assert.Equal(1L, casts[0].Request.TargetPlayerId); // 显式仇恨目标（D-P5-4）
    }

    [Fact]
    public void Damage_Triggers_Hit_Then_Death_Removal_And_Respawn()
    {
        var (store, module) = Create();
        var slimeId = MonsterModule.NpcIdSeed + 2;
        var slime = store.GetPlayer(slimeId)!;

        store.GetPlayer(slimeId)!.ApplyDamage(100f); // 玩家裁决扣血（T3 链）——Hp 0 死亡
        module.Tick(0);

        var state = module.GetState(slimeId);
        Assert.NotNull(state);
        Assert.True(state!.DeathTick.HasValue);
        Assert.Equal((byte)NpcAnimationState.Death, slime.ActionState);

        for (uint t = 1; t < MonsterModule.DeathDisplayTicks; t++) module.Tick(t);
        Assert.NotNull(store.GetPlayer(slimeId)); // 展示窗口内仍在（移除在 t=30）

        module.Tick(MonsterModule.DeathDisplayTicks);
        Assert.Null(store.GetPlayer(slimeId));                    // 移除
        Assert.Contains(slimeId, module.DrainRemovedNpcs());      // Server3C 清 AOI 用

        // 重生窗口：dueTick = DeathDisplayTicks + RespawnTicks = 480，到期后新 id 满血重生在巡逻中心
        for (uint t = MonsterModule.DeathDisplayTicks + 1; t < MonsterModule.DeathDisplayTicks + MonsterModule.RespawnTicks; t++)
            module.Tick(t);
        module.Tick(MonsterModule.DeathDisplayTicks + MonsterModule.RespawnTicks);

        Assert.Equal(4, module.Count); // 重生完成（Count 恢复）
    }
}

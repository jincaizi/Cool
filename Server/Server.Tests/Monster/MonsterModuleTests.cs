using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Config;
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

    [Fact]
    public void Target_Lost_Triggers_Return_To_Spawn_And_Peace_Reset()
    {
        var (store, module) = Create();
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f)); // Wolf[0] (12,0,0) 面向原点——开局即仇恨
        long wolfId = MonsterModule.NpcIdSeed + 1;
        var wolf = store.GetPlayer(wolfId)!;

        for (uint t = 0; t < 200; t++) module.Tick(t);
        Assert.Equal(AlertLevel.HOSTILE, module.GetState(wolfId)!.AlertLevel); // 前置：已获取仇恨
        Assert.True(Vector3.Distance(wolf.Position, new Vector3(0f, 0f, 0f)) <= 2.5f,
            "前置：狼应已追至攻击距离");

        store.RemovePlayer(1); // 目标消失（离场/死亡移除——DetectTarget 目标丢失路径）

        bool returnedToPeace = false;
        for (uint t = 200; t < 400 && !returnedToPeace; t++)
        {
            module.Tick(t);
            returnedToPeace = module.GetState(wolfId)!.AlertLevel == AlertLevel.PEACE;
        }

        Assert.True(returnedToPeace, "wolf should walk back to spawn and reset to PEACE after target loss");
        float distToSpawn = Vector3.Distance(wolf.Position, new Vector3(12f, 0f, 0f));
        Assert.True(distToSpawn <= 0.5f, $"wolf should be back at spawn, dist={distToSpawn}");
    }

    [Fact]
    public void Hit_Stuns_Monster_For_Display_Window_Then_Resumes_Patrol()
    {
        var (store, module) = Create();
        long slimeId = MonsterModule.NpcIdSeed + 2; // Slime (40,0,0)
        var slime = store.GetPlayer(slimeId)!;

        const uint t0 = 10;
        for (uint t = 0; t < t0; t++) module.Tick(t);
        Assert.Equal((byte)NpcAnimationState.Run, slime.ActionState); // 前置：巡逻移动中

        slime.ApplyDamage(10f); // 玩家裁决扣血（T3 链）——HP 90 < LastHp 100 → 下一 tick 转 Hit
        module.Tick(t0);
        Assert.Equal((byte)NpcAnimationState.Hit, slime.ActionState);
        var posAtHit = slime.Position;

        // 硬直窗口（HitDisplayTicks：t0..t0+4）：行为树被跳过——位置冻结
        for (uint t = t0 + 1; t < t0 + MonsterModule.HitDisplayTicks; t++)
        {
            module.Tick(t);
            Assert.Equal(posAtHit.X, slime.Position.X);
            Assert.Equal(posAtHit.Z, slime.Position.Z);
        }

        // 窗口过后恢复（位置重新变化——巡逻继续）
        for (uint t = t0 + MonsterModule.HitDisplayTicks; t < t0 + MonsterModule.HitDisplayTicks + 10; t++)
            module.Tick(t);
        Assert.True(Vector3.Distance(posAtHit, slime.Position) > 0.1f,
            "monster should resume moving after the hit window elapses");
    }

    [Fact]
    public void Respawn_Uses_New_Id_And_Full_Hp()
    {
        var (store, module) = Create();
        long slimeId = MonsterModule.NpcIdSeed + 2; // Slime (40,0,0)，config hp=100
        var initialIds = new HashSet<long>();
        foreach (var p in store.All) initialIds.Add(p.PlayerId);
        Assert.Contains(slimeId, initialIds);

        store.GetPlayer(slimeId)!.ApplyDamage(100f); // 致死
        for (uint t = 0; t <= MonsterModule.DeathDisplayTicks + MonsterModule.RespawnTicks; t++)
            module.Tick(t);

        long? respawnedId = null;
        foreach (var p in store.All)
            if (p.Kind == (byte)EntityKind.Npc && !initialIds.Contains(p.PlayerId)) { respawnedId = p.PlayerId; break; }

        Assert.NotNull(respawnedId);                        // 重生已发生（新实体入池）
        Assert.NotEqual(slimeId, respawnedId!.Value);       // 新 id 而非复用（id 单调——缓存/AOI 键安全）
        var respawned = store.GetPlayer(respawnedId.Value)!;
        Assert.Equal(1, respawned.TemplateId);              // 同模板重生
        Assert.Equal(respawned.MaxHp, respawned.Hp);        // 满血（HP 权威重置）
        Assert.Equal(100f, respawned.Hp);                   // Slime config hp=100
    }

    [Fact]
    public void DefaultLayout_Carries_Six_Templates_In_Order()
    {
        var (store, _) = Create(6);

        var templates = store.All
            .Where(e => e.Kind == (byte)EntityKind.Npc)
            .OrderBy(e => e.PlayerId)
            .Select(e => e.TemplateId)
            .ToArray();

        Assert.Equal(new[] { 2, 1, 1, 2, 3, 4 }, templates);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void New_Templates_Can_Spawn(int templateId)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);

        var npcId = module.SpawnMonster(templateId, new Vector3(5f, 0f, 5f), new Vector3(5f, 0f, 5f));

        Assert.True(npcId > 0);
        var npc = store.GetPlayer(npcId);
        Assert.NotNull(npc);
        Assert.Equal(templateId, npc!.TemplateId);
    }

    /// <summary>防「静默冻结」：怪物施法形状范围取 Config.attackRange（MonsterBtActions），
    /// 超过表上限会被 CombatRules.ValidateShape 拒收，而拒收只写审计日志——怪物在攻击距离上零表现。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Template_AttackRange_Fits_Its_Combat_Rule(int templateId)
    {
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Config", "monster_config.json");
        var config = ConfigLoader.LoadMonsterTemplates(path)[templateId];
        var skillId = MonsterModule.SkillIdOf(templateId);

        Assert.True(CombatRules.TryGet(skillId, out var rule), $"template {templateId} 缺 {skillId} 表项");
        Assert.True(config.attackRange <= rule!.MaxRange + 0.01f,
            $"template {templateId} attackRange={config.attackRange} > skill {skillId} maxRange={rule.MaxRange}");
    }

    /// <summary>DemoDay 玩家出生点——客户端 `PlayerSpawner._spawnPoint` 实测
    ///（DemoDay.unity 里该 Transform 无父节点，local 即 world (10, 0.16, -15.52)；
    /// y 与感知判定无关——TargetDetector 只算 XZ——故常量 y 取 0，使 Vector3.Distance 即 XZ 距离）。
    /// **不是原点**：原点只是 e2e 观察者的位置，按原点推点位会漏判（2026-10-12 实测教训）。</summary>
    private static readonly Vector3 DemoSpawnPosition = new Vector3(10f, 0f, -15.52f);

    /// <summary>挂机安全：新增槽位距出生点须 > 该模板「感知半径 + 巡逻半径」——否则怪物走完巡逻圈
    /// 就能感知到站在原地不动的玩家。半径从构建输出目录的 monster_config.json 读，不在测试里抄数字。
    /// 只守 [4]/[5]：前四点位是既有 e2e 夹具（[0] Wolf 面向原点即仇恨，设计如此），改点位会动到 e2e 假设；
    /// 且 [0] 的巡逻圈最近点距出生点约 5.9m < 检测 15m——挂机挨打的是它，不是这两只新怪。
    /// 实测教训：DogKnight 初版 (0,0,-20) 距出生点仅 11.0m < 感知 18m → 12 分钟内 8/8 次进世界打死挂机玩家。</summary>
    [Theory]
    [InlineData(4)] // DogKnight(t3)
    [InlineData(5)] // SpiderBoss(t4)
    public void New_Layout_Slots_Stay_Out_Of_Reach_Of_Demo_Spawn(int slot)
    {
        var (store, _) = Create(slot + 1);
        var npc = store.All
            .Where(e => e.Kind == (byte)EntityKind.Npc)
            .OrderBy(e => e.PlayerId)
            .ElementAt(slot);

        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Config", "monster_config.json");
        var config = ConfigLoader.LoadMonsterTemplates(path)[npc.TemplateId];

        float dist = Vector3.Distance(DemoSpawnPosition, npc.Position);
        float reach = config.detectionRadius + config.patrolRadius;
        Assert.True(dist > reach,
            $"槽位 [{slot}] 模板 {npc.TemplateId} 距出生点 {dist:F1}m ≤ 感知+巡逻 {reach}m——挂机玩家会被它开局追杀");
    }

    /// <summary>上面那条几何断言的行为侧（活体实测的廉价替身，2026-10-12）：玩家长时间挂着不动，
    /// 两只新怪一次施法都不该发起——从感知、行为树到入队整条链上验一遍，不只看距离。
    /// 只盯 50003/50004：槽位 [0] Wolf 本来就会来（见上条注释），不在本测试的守备范围。
    /// 伤害裁决不在本模块（Server3C drain 后才掷骰），故这里玩家不掉血、施法队列是本模块唯一出口。</summary>
    [Fact]
    public void Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_The_New_Monsters()
    {
        var (store, module) = Create(6);
        LoginPlayer(store, 1, DemoSpawnPosition); // 站着不动 40s（600 tick，够 Wolf 走完大半个巡逻圈）

        var castSkills = new List<int>();
        for (uint t = 0; t < 600; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts()) castSkills.Add(request.SkillId);
        }

        Assert.Equal(6, module.Count); // 六只都在场——防"没生成所以没施法"的假绿
        Assert.DoesNotContain(50003, castSkills);
        Assert.DoesNotContain(50004, castSkills);
    }
}

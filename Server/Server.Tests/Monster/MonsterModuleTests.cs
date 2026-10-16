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
        var slime = store.GetPlayer(MonsterModule.NpcIdSeed + 2)!; // 布局 [1]：Slime (0,0,-20)
        var start = slime.Position;

        for (uint t = 0; t < 60; t++) module.Tick(t);

        Assert.NotEqual(start, slime.Position);                       // 巡逻移动
        float dx = slime.Position.X - 0f, dz = slime.Position.Z - (-20f);
        Assert.True(dx * dx + dz * dz <= 5f * 5f + 0.01f);           // 巡逻半径 5 内（D-P5-9 零玩家持续运转）
    }

    [Fact]
    public void Detection_Aggro_And_Attack_Queue_Targets_Closest_Player()
    {
        var (store, module) = Create();
        // Wolf[0] (-30,0,0) 面向原点（+x 朝向）——玩家在其前方 10m（检测 15m ≥ 10m，90° 锥内）
        LoginPlayer(store, 1, new Vector3(-20f, 0f, 0f));
        var wolf = store.GetPlayer(MonsterModule.NpcIdSeed + 1)!;

        for (uint t = 0; t < 200; t++) module.Tick(t); // 3.5m/s × 200×66ms ≈ 46m 行程——足够追到 2m 内

        float dist = Vector3.Distance(wolf.Position, new Vector3(-20f, 0f, 0f));
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
        // Wolf[0] (-30,0,0) 面向原点——玩家在其前方 10m（检测 15m ≥ 10m）：开局即仇恨
        LoginPlayer(store, 1, new Vector3(-20f, 0f, 0f));
        long wolfId = MonsterModule.NpcIdSeed + 1;
        var wolf = store.GetPlayer(wolfId)!;

        for (uint t = 0; t < 200; t++) module.Tick(t);
        Assert.Equal(AlertLevel.HOSTILE, module.GetState(wolfId)!.AlertLevel); // 前置：已获取仇恨
        Assert.True(Vector3.Distance(wolf.Position, new Vector3(-20f, 0f, 0f)) <= 2.5f,
            "前置：狼应已追至攻击距离");

        store.RemovePlayer(1); // 目标消失（离场/死亡移除——DetectTarget 目标丢失路径）

        bool returnedToPeace = false;
        for (uint t = 200; t < 400 && !returnedToPeace; t++)
        {
            module.Tick(t);
            returnedToPeace = module.GetState(wolfId)!.AlertLevel == AlertLevel.PEACE;
        }

        Assert.True(returnedToPeace, "wolf should walk back to spawn and reset to PEACE after target loss");
        float distToSpawn = Vector3.Distance(wolf.Position, new Vector3(-30f, 0f, 0f));
        Assert.True(distToSpawn <= 0.5f, $"wolf should be back at spawn, dist={distToSpawn}");
    }

    [Fact]
    public void Hit_Stuns_Monster_For_Display_Window_Then_Resumes_Patrol()
    {
        var (store, module) = Create();
        long slimeId = MonsterModule.NpcIdSeed + 2; // Slime (0,0,-20)
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

    /// <summary>
    /// 硬直窗口到期那一 tick，ActionState 必须离开 Hit（终审 I1，产品缺陷）。
    /// 机制：窗口内跳过行为树；窗口一过期 BT 立刻恢复，**但 Chase 在攻击距离内 `return Success`
    /// 不写状态（MonsterBtActions:42）、Attack 在冷却门未过时 `return Running` 也不写（:68）**——
    /// 近战互殴（目标全程留在攻击距离内）时 ActionState 会从受击那刻一直停在 Hit，直到下一次
    /// 真正施法（该怪冷却 1.5s / 2.0s）或目标离开攻击距离。客户端持续采到 Hit → 驱动只写一次 →
    /// Animator 停在 Hit 态：DogKnight 的 GetHit 是循环片段（循环播）、蜘蛛的 hit1 非循环（定格末帧）。
    /// 走**真实裁决链**（drain + CombatResolver）让冷却记录生效——否则怪物每 tick 都能施法，
    /// 到期那一 tick 恰好会被 Attack 写成 Attack，缺陷测不出来（假绿）。
    /// </summary>
    [Fact]
    public void Hit_Window_Expiry_Restores_Locomotion_While_Target_Stays_In_Attack_Range()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        // Wolf：攻击距离 2.0m，冷却 15 tick（1s）。生成在 1.5m 处且面朝原点（SpawnMonster 的朝向
        // 恒面向原点）——玩家原地不动，全程留在攻击距离内（正是缺陷的触发条件）
        long wolfId = module.SpawnMonster(2, new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.5f));
        var wolf = store.GetPlayer(wolfId)!;
        LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));

        var resolver = new CombatResolver(store, new System.Random(42));

        // 前置：让怪物真施放一次（冷却记录由裁决链写入——BT 的冷却门读的正是它）
        uint castTick = 0;
        bool casted = false;
        for (uint t = 0; t < 10 && !casted; t++)
        {
            module.Tick(t);
            foreach (var (id, request) in module.DrainPendingCasts())
            {
                resolver.Decide(id, request, t);
                resolver.DrainResolved(t);      // 推进队列（本用例不消费结果；判定延迟 >0 ⇒ 决策当 tick 取不到这一条）
                castTick = t;
                casted = true;
            }
        }
        Assert.True(casted, "前置：怪物应在攻击距离内施放一次（入队 + 裁决链）");
        Assert.Equal((byte)NpcAnimationState.Attack, wolf.ActionState);

        // 玩家命中怪物 → 下一 tick 进受击硬直
        uint hitTick = castTick + 1;
        wolf.ApplyDamage(10f);
        module.Tick(hitTick);
        Assert.Equal((byte)NpcAnimationState.Hit, wolf.ActionState);

        // 推进越过窗口（hitTick + HitDisplayTicks = 刚出窗口的那一 tick，再往后两 tick）
        for (uint t = hitTick + 1; t <= hitTick + MonsterModule.HitDisplayTicks + 2; t++)
        {
            module.Tick(t);
            // 前置：冷却门全程关着（没有新施法来"顺手"把状态写离 Hit）——缺陷场景成立
            Assert.Empty(module.DrainPendingCasts());
        }

        Assert.NotEqual((byte)NpcAnimationState.Hit, wolf.ActionState);
    }

    [Fact]
    public void Respawn_Uses_New_Id_And_Full_Hp()
    {
        var (store, module) = Create();
        long slimeId = MonsterModule.NpcIdSeed + 2; // Slime (0,0,-20)，config hp=100
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
    ///（DemoDay.unity 里该 Transform 无父节点，local 即 world (-15, 0.3, -30)；
    /// y 与感知判定无关——TargetDetector 只算 XZ——故常量 y 取 0，使 Vector3.Distance 即 XZ 距离）。
    /// **不是原点**：原点只是 e2e 观察者的位置，按原点推点位会漏判（2026-10-12 实测教训）。
    /// 2026-10-13 第二次搬迁：从 (10,0,-15.52) 搬到 (10,0,-40)（湖东岸台地，地面 y≈0.21 高于水面 -0.38）——
    /// 旧点距 [0] Wolf 的巡逻圈最近只有 5.9m（检测 15m），站着不动必被它打死。
    /// 2026-10-17 第三次搬迁：搬到 (-15,0.3,-30)（地面实测 y≈0.256，y 抬到 0.3 让胶囊落稳）——
    /// 与布局收紧同批（湖底/贴边的槽位收回陆地并向出生点靠拢——玩家反馈"怪太散、有的出图"）。
    /// 前两次选点与验证见 docs/superpowers/plans/2026-10-13-spawn-and-collision-report.md，
    /// 第三次见 production/verify/layout-and-spawn-move.md。</summary>
    private static readonly Vector3 DemoSpawnPosition = new Vector3(-15f, 0f, -30f);

    /// <summary>挂机安全：每个槽位距出生点须 > 该模板「感知半径 + 巡逻半径」——否则怪物走完巡逻圈
    /// 就能感知到站在原地不动的玩家。半径从构建输出目录的 monster_config.json 读，不在测试里抄数字。
    /// **全六槽位**（2026-10-13 起）：出生点搬到湖东岸后 [0] Wolf 也满足该规则了——旧出生点下它的
    /// 巡逻圈最近点只有约 5.9m（&lt; 检测 15m），是按设计违反本规则的唯一槽位，当时只能排除它。
    /// 2026-10-17 第三次搬迁（出生点 + 布局同批改）后仍全六槽位达标，最小余量 [2] Slime +2.8m。
    /// 实测教训：DogKnight 初版 (0,0,-20) 距出生点仅 11.0m < 感知 18m → 12 分钟内 8/8 次进世界打死挂机玩家。</summary>
    [Theory]
    [InlineData(0)] // Wolf(t2)
    [InlineData(1)] // Slime(t1)
    [InlineData(2)] // Slime(t1)
    [InlineData(3)] // Wolf(t2)
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

    /// <summary>上面那条几何断言的行为侧（活体实测的廉价替身，2026-10-12；2026-10-13 扩到全六槽位）：
    /// 玩家长时间挂着不动，**任何**怪一次施法都不该发起——从感知、行为树到入队整条链上验一遍，不只看距离。
    /// 2700 tick = 3 分钟（与 2026-10-13 的挂机活体验证同窗口；Wolf 巡逻一圈约 16s，够走 11 圈）。
    /// 出生点搬到湖东岸前，槽位 [0] Wolf 会主动来（旧点下它按设计违反几何规则），当时只能盯 50003/50004。
    /// 伤害裁决不在本模块（Server3C drain 后才掷骰），故这里玩家不掉血、施法队列是本模块唯一出口。</summary>
    [Fact]
    public void Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_Any_Monster()
    {
        var (store, module) = Create(6);
        LoginPlayer(store, 1, DemoSpawnPosition); // 站着不动 3 分钟

        var castSkills = new List<int>();
        for (uint t = 0; t < 2700; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts()) castSkills.Add(request.SkillId);
        }

        Assert.Equal(6, module.Count); // 六只都在场——防"没生成所以没施法"的假绿
        Assert.Empty(castSkills);
    }

    /// <summary>F19：客户端 `NpcEntityAnimator` 的忙位解除**依赖**"决策与置 ActionState=Attack
    /// 发生在同一次 BT 动作内"（否则挥砍被锁定在 Attack 态不释放）。该不变量此前已被
    /// `Hit_Window_Expiry_Restores_Locomotion_While_Target_Stays_In_Attack_Range` **间接**覆盖
    ///（它 :210 的前置断言在决策 tick 断言同一等式，破坏本不变量时它同样会红）；
    /// 本用例把它钉成**最小脚手架的专用钉**——不需要裁决链/伤害铺垫，两处写入被拆到不同 tick 时
    /// 直接在 :384 失败。</summary>
    [Fact]
    public void Attack_Decision_And_Action_State_Happen_In_The_Same_Tick()
    {
        var (store, module) = Create();
        // Wolf[0] (-30,0,0) 面向原点——玩家在其前方 10m（检测 15m）：追进射程后出手
        LoginPlayer(store, 1, new Vector3(-20f, 0f, 0f));
        var wolf = store.GetPlayer(MonsterModule.NpcIdSeed + 1)!;

        uint decideTick = 0;
        for (uint t = 0; t < 200 && decideTick == 0; t++)
        {
            module.Tick(t);
            if (module.DrainPendingCasts().Count > 0)
            {
                decideTick = t;
                // 同一次 BT 动作的产物：入队的那一 tick 就必须已经是 Attack 态
                Assert.Equal((byte)NpcAnimationState.Attack, wolf.ActionState);
            }
        }
        Assert.NotEqual(0u, decideTick);
        Assert.True(module.GetState(MonsterModule.NpcIdSeed + 1)!.AttackResolveTick.HasValue);
    }
}

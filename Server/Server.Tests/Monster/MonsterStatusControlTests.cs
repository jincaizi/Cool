using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

/// <summary>Plan 04 任务 5：怪物读取 ControlSpeedScale/ControlActionLock、麻痹按霸体打断、
/// Evade 到家清理状态并恢复。模块级用例不接 sink（直写 PlayerState）；服务器级用例
/// 走 EnableMonsters 的 control sink，验证状态运行时确实触发 CancelAction。</summary>
public class MonsterStatusControlTests
{
    private static ElementalCatalogDefinition Catalog() => ElementalCatalogDefaults.Create();

    private static StatusEffectDefinition SlowOf(ElementalCatalogDefinition catalog)
        => catalog.Statuses.Find(s => s.EffectId == 101)!;

    private static StatusEffectDefinition ParalyzeOf(ElementalCatalogDefinition catalog)
        => catalog.Statuses.Find(s => s.EffectId == 301)!;

    private static (EntityStore Store, MonsterModule Module, long Id) MakeModule(int template = 1)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, actionV1: true);
        long id = module.SpawnMonster(template, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
        return (store, module, id);
    }

    private static PlayerState LoginPlayer(EntityStore store, long id, Vector3 position)
    {
        var player = store.AddPlayer(id, $"P{id}");
        player.IsLoggedIn = true;
        player.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return player;
    }

    private static void ApplyParalyze(Server3C server, PlayerState target, long sourceId, uint tick)
    {
        var catalog = server.ElementalCatalog;
        var result = target.Statuses!.Apply(ParalyzeOf(catalog), sourceId, 10002, 7UL, tick, catalog);
        Assert.Equal(StatusApplyResult.Applied, result);
    }

    // ---------- 冰减速：UpdatePosition.Speed = moveSpeed × SpeedScale ----------

    [Fact]
    public void Slow_Scales_Monster_Position_Speed_By_ControlSpeedScale()
    {
        var (store, module, id) = MakeModule();
        var npc = store.GetPlayer(id)!;
        var catalog = Catalog();
        npc.ConfigureStatuses(catalog);

        Assert.Equal(StatusApplyResult.Applied,
            npc.Statuses!.Apply(SlowOf(catalog), sourceId: 1, skillId: 10002, castId: 7UL, tick: 0, catalog));
        Assert.Equal(0.7f, npc.ControlSpeedScale, 4);

        float moveSpeed = module.GetState(id)!.Config.moveSpeed;
        uint t = 0;
        var before = npc.Position;
        // 巡逻起步（本 tick 未动时 Speed=0）
        while (t < 29 && npc.Speed == 0f) { before = npc.Position; module.Tick(t); t++; }

        Assert.True(npc.Speed > 0f, "precondition: 怪物应在巡逻移动");
        Assert.Equal(moveSpeed * 0.7f, npc.Speed, 3);

        // 不是只缩广播字段：SimpleMoveSystem 实际积分出的位移也按 SpeedScale 缩小
        //（ORCA 无邻居时期望位移逐字返回）。
        float step = Vector3.Distance(before, npc.Position);
        Assert.Equal(moveSpeed * 0.7f * MonsterModule.TickDeltaSeconds, step, 3);
    }

    // ---------- 雷：ActionLock 冻结行为树，到期恢复 ----------

    [Fact]
    public void Paralyze_Freezes_Behavior_Tree_And_Expiry_Resumes_Attack()
    {
        var (store, module, id) = MakeModule();
        var npc = store.GetPlayer(id)!;
        var player = LoginPlayer(store, 1, new Vector3(0f, 0f, 1f));
        var state = module.GetState(id)!;
        state.Mode = MonsterMode.Combat;
        state.Threats.AddDamage(1, 10f);

        var catalog = Catalog();
        npc.ConfigureStatuses(catalog);
        Assert.Equal(StatusApplyResult.Applied,
            npc.Statuses!.Apply(ParalyzeOf(catalog), sourceId: 1, skillId: 10002, castId: 7UL, tick: 0, catalog));
        Assert.True(npc.ControlActionLock);

        // 0.8s = 12 tick：行为树整段跳过——不移动、不施法。
        var frozenPosition = npc.Position;
        for (uint t = 0; t < 12; t++)
        {
            module.Tick(t);
            Assert.Empty(module.DrainPendingCasts());
            Assert.Equal(0f, npc.Speed);
            Assert.Equal(frozenPosition.X, npc.Position.X);
            Assert.Equal(frozenPosition.Z, npc.Position.Z);
        }

        npc.Statuses.RemoveExpired(12); // 主循环 preTick 的到期清理
        Assert.False(npc.ControlActionLock);
        Assert.Equal(1f, npc.ControlSpeedScale, 4);

        bool casted = false;
        for (uint t = 12; t < 20 && !casted; t++)
        {
            module.Tick(t);
            casted = module.DrainPendingCasts().Count > 0;
        }
        Assert.True(casted, "paralysis expired: monster should attack again (target 1m away)");
        Assert.Equal(1L, state.TargetId);
        Assert.Equal(player.PlayerId, state.TargetId!.Value);

        // 移动同样恢复：把玩家移出感知、回 Patrol，下一 tick 应有实际位移且速度回到未缩放的 moveSpeed。
        player.UpdatePosition(new Vector3(0f, 0f, 100f), 0f, 0f, 2L, 0U);
        state.Mode = MonsterMode.Patrol;
        state.TargetId = null;
        state.LastKnownTargetPosition = null;
        state.Threats.Clear();
        var beforeMove = npc.Position;
        module.Tick(25);
        Assert.True(Vector3.Distance(beforeMove, npc.Position) > 0.01f,
            "paralysis expired: patrol movement should resume");
        Assert.Equal(state.Config.moveSpeed, npc.Speed, 3);
    }

    // ---------- 麻痹打断：宿主 sink → CancelAction（霸体只免打断） ----------

    [Fact]
    public void Paralyze_Cancels_Unarmored_Windup_Through_Host_Sink()
    {
        using var server = new Server3C(new TestLogger());
        server.EnableMonsters(6);
        var player = server.RegisterEntity(1, "P");
        var wolf = server.GetEntity(MonsterModule.NpcIdSeed + 1)!; // 布局 [0]：Wolf(t2)
        player.UpdatePosition(new Vector3(-30f, 0f, 1f), 0f, 0f, 1L, 0U);

        var runner = server.MonsterModuleForTest!.GetState(wolf.PlayerId)!.ActionRunner!;
        Assert.True(runner.TryBeginSkill(50002, 1, 0));
        Assert.Equal(NpcActionPhase.Windup, runner.Current.Phase);
        Assert.False(runner.IsArmored);

        ApplyParalyze(server, wolf, sourceId: 1, tick: 0);

        Assert.True(wolf.ControlActionLock);
        Assert.Equal(NpcActionPhase.Cancelled, runner.Current.Phase);
        Assert.Equal(NpcActionCancelReason.Hit, runner.Current.CancelReason);
    }

    [Fact]
    public void Paralyze_Does_Not_Cancel_Armored_Windup_But_Status_Still_Attaches()
    {
        using var server = new Server3C(new TestLogger());
        server.EnableMonsters(6);
        var player = server.RegisterEntity(1, "P");
        var boss = server.GetEntity(MonsterModule.NpcIdSeed + 6)!; // 布局 [5]：SpiderBoss(t4)
        Assert.Equal(4, boss.TemplateId);
        player.UpdatePosition(new Vector3(18.333f, 0f, 19.389f), 0f, 0f, 1L, 0U);

        var runner = server.MonsterModuleForTest!.GetState(boss.PlayerId)!.ActionRunner!;
        Assert.True(runner.TryBeginSkill(50004, 1, 0));
        Assert.Equal(NpcActionPhase.Windup, runner.Current.Phase);
        Assert.True(runner.IsArmored); // 模板 4 的 Ai.DefaultWindupArmor

        ApplyParalyze(server, boss, sourceId: 1, tick: 0);

        Assert.True(boss.ControlActionLock);                       // 状态仍挂上（同步控制）
        Assert.Equal(1, boss.Statuses!.Runtime.Count);
        Assert.Equal(NpcActionPhase.Windup, runner.Current.Phase); // 首版：霸体只免打断
    }

    // ---------- 重生代次：生成回调接 sink，麻痹仍能取消未提交前摇 ----------

    [Fact]
    public void Respawned_Monster_Gets_Control_Sink_And_Paralyze_Cancels_Windup()
    {
        using var server = new Server3C(new TestLogger());
        server.EnableMonsters(1); // 布局 [0]：Wolf (-30,0,0)
        var player = server.RegisterEntity(1, "P");
        player.UpdatePosition(new Vector3(-30f, 0f, 1f), 0f, 0f, 1L, 0U);

        var module = server.MonsterModuleForTest!;
        long firstId = MonsterModule.NpcIdSeed + 1;
        var first = server.GetEntity(firstId)!;
        first.ApplyDamage(first.MaxHp + 1f); // 致死 → 下一 tick 进死亡展示

        // 死亡展示 30 tick + 重生 450 tick（与 MonsterModuleTests 同口径）
        for (uint t = 0; t <= MonsterModule.DeathDisplayTicks + MonsterModule.RespawnTicks; t++)
            module.Tick(t);

        long respawnId = MonsterModule.NpcIdSeed + 2;
        var npc = server.GetEntity(respawnId);
        Assert.NotNull(npc);                                   // 重生完成
        Assert.Equal(2, npc!.TemplateId);                      // 同模板新 id
        Assert.Null(module.GetState(firstId));                 // 旧代次已移除（防假绿）
        Assert.NotNull(npc.Statuses);                          // OnMonsterSpawned 已配置状态运行时

        uint castTick = MonsterModule.DeathDisplayTicks + MonsterModule.RespawnTicks + 1;
        var runner = module.GetState(respawnId)!.ActionRunner!;
        player.UpdatePosition(new Vector3(-30f, 0f, 1f), 0f, 0f, 2L, 0U);
        Assert.True(runner.TryBeginSkill(50002, 1, castTick));
        Assert.Equal(NpcActionPhase.Windup, runner.Current.Phase);

        ApplyParalyze(server, npc, sourceId: 1, tick: castTick + 1);

        Assert.True(npc.ControlActionLock);
        Assert.Equal(NpcActionPhase.Cancelled, runner.Current.Phase); // 重生代次同样走 CancelAction
        Assert.Equal(NpcActionCancelReason.Hit, runner.Current.CancelReason);
    }

    // ---------- Evade 到家：清状态、回满血、恢复移动 ----------

    [Fact]
    public void Evade_ArriveHome_Clears_Status_And_Restores_Patrol_Movement()
    {
        var (store, module, id) = MakeModule();
        var npc = store.GetPlayer(id)!;
        var state = module.GetState(id)!;
        var catalog = Catalog();
        npc.ConfigureStatuses(catalog);
        npc.Statuses!.Apply(SlowOf(catalog), sourceId: 1, skillId: 10002, castId: 7UL, tick: 0, catalog);
        Assert.Equal(0.7f, npc.ControlSpeedScale, 4);
        npc.ApplyDamage(npc.MaxHp * 0.5f); // 先掉血，回满断言才有意义

        state.Mode = MonsterMode.Combat;
        state.TargetId = null;
        state.Position = new Vector3(0f, 0f, 25f); // > Slime leash 23

        uint t = 0;
        while (t < 600 && state.Mode != MonsterMode.Patrol) { module.Tick(t); t++; }

        Assert.Equal(MonsterMode.Patrol, state.Mode);
        Assert.Empty(npc.Statuses.Runtime.Active);
        Assert.Equal(1f, npc.ControlSpeedScale, 4);
        Assert.False(npc.ControlActionLock);
        Assert.Equal(npc.MaxHp, npc.Hp, 3);

        bool moved = false;
        for (uint k = t; k < t + 5 && !moved; k++) { module.Tick(k); moved = npc.Speed > 0f; }
        Assert.True(moved, "arrive home should restore patrol movement");
    }

    // ---------- 玩家控制不受怪物霸体规则影响 ----------

    [Fact]
    public void Player_Control_Lock_Does_Not_Touch_Monster_Actions()
    {
        using var server = new Server3C(new TestLogger());
        server.EnableMonsters(1);
        var player = server.RegisterEntity(1, "P");
        player.UpdatePosition(new Vector3(-30f, 0f, 1f), 0f, 0f, 1L, 0U);
        var wolf = server.GetEntity(MonsterModule.NpcIdSeed + 1)!;
        var runner = server.MonsterModuleForTest!.GetState(wolf.PlayerId)!.ActionRunner!;
        Assert.True(runner.TryBeginSkill(50002, 1, 0));

        ApplyParalyze(server, player, sourceId: 1, tick: 0);

        Assert.True(player.ControlActionLock);                    // 玩家自己锁定
        Assert.False(wolf.ControlActionLock);                     // 不串到怪物控制状态
        Assert.Equal(NpcActionPhase.Windup, runner.Current.Phase); // 不触发 CancelAction
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// Plan 06 任务 4：AOI 快速出入 / 死亡→复活换命 / Evade 回巢与回池重生 / 观察者断线重进
/// 下的版本连续性。判据直接比较基线 <see cref="CombatProjection"/> 的
/// LifeId/CombatVersion/Hp/Statuses/EquipmentSummary 与服务器实体当前状态，
/// 并用 AoiBroadcaster 的实际发送计数锁定"视野外/旧生命周期后不得再投递旧增量"。
/// </summary>
public class AoiVersionContinuityTests
{
    [Fact]
    public void Rapid_Hysteresis_Reentry_Baselines_Carry_Current_Version_And_No_Sends_While_Out()
    {
        // 进出边界：进 ≤80m、出须 >90m（迟滞 80~90m）。每轮出界期间推进战斗状态，
        // 重见基线必须携带当前版本/代次/血量/状态/装备；出界后不得再发任何旧增量。
        var catalog = ElementalCatalogDefaults.Create();
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        MoveTo(observer, 0f);
        var target = store.AddPlayer(2, "Target");
        MoveTo(target, 70f); // 远带（60~80m）起步——80/90/91 的往返不跨带，避免带切换帧干扰计数
        target.ConfigureStatuses(catalog);
        target.EquippedWeaponInstanceId = 42;
        target.EquippedWeaponTemplateId = ElementalCatalogDefaults.IceSwordTemplateId;
        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        target.Statuses!.Apply(burn, sourceId: 1, skillId: 10002, castId: 7, tick: 0);

        var broadcaster = NewBroadcaster(catalog);
        var capture = CaptureOf(broadcaster);
        uint tick = 1;

        Assert.Single(broadcaster.BuildForObserver(observer, tick++, store));
        AssertBaselineMatchesServer(capture.Last, target, catalog);
        Assert.Equal(1, capture.Baselines.Count);

        var templates = new[]
        {
            ElementalCatalogDefaults.FireSwordTemplateId,
            ElementalCatalogDefaults.ThunderSwordTemplateId,
            ElementalCatalogDefaults.WindSwordTemplateId
        };

        for (int cycle = 0; cycle < 3; cycle++)
        {
            int beforeCycle = capture.Sent.Count;
            // 出界路径：80（进入）→ 90（迟滞保持）→ 91（>90m 离开）
            MoveTo(target, 80f);
            broadcaster.BuildForObserver(observer, tick++, store);
            Assert.True(IsVisible(broadcaster, store, observer, target), "80m 必须可见（进入半径闭区间）");
            MoveTo(target, 90f);
            broadcaster.BuildForObserver(observer, tick++, store);
            Assert.True(IsVisible(broadcaster, store, observer, target), "90m 在迟滞带内必须保持可见");
            Assert.Equal(beforeCycle, capture.Sent.Count); // 远带 4 tick 节拍内，位移不产生增量帧

            MoveTo(target, 91f);
            broadcaster.BuildForObserver(observer, tick++, store);
            Assert.False(IsVisible(broadcaster, store, observer, target), "91m 已超出迟滞半径必须离开");

            int frozen = capture.Sent.Count;
            // 视野外推进状态：旧增量不得投递给本观察者
            target.ApplyDamage(5f);
            target.EquippedWeaponInstanceId = 100 + cycle;
            target.EquippedWeaponTemplateId = templates[cycle];
            if (cycle == 2) target.Statuses.ClearAll(tick); // 最后一轮清状态：基线状态列表须跟上
            broadcaster.BuildForObserver(observer, tick++, store);
            broadcaster.BuildForObserver(observer, tick++, store);
            Assert.Equal(frozen, capture.Sent.Count);

            // 回程 90m 仍不可见——重进必须降到 ≤80m
            MoveTo(target, 90f);
            broadcaster.BuildForObserver(observer, tick++, store);
            Assert.False(IsVisible(broadcaster, store, observer, target), "出界后 90m 不得凭旧迟滞状态重见");
            Assert.Equal(frozen, capture.Sent.Count);

            MoveTo(target, 80f);
            broadcaster.BuildForObserver(observer, tick++, store);
            Assert.Equal(frozen + 1, capture.Sent.Count);
            AssertBaselineMatchesServer(capture.Last, target, catalog);
            Assert.Equal(cycle + 2, capture.Baselines.Count);
        }

        // 3 次进出 = 3 条重见基线 + 首帧基线；视野外零增量。
        Assert.Equal(4, capture.Sent.Count);
        Assert.Equal(4, capture.Baselines.Count);
        Assert.All(capture.Sent, s => Assert.Equal(2L, s.PlayerId));
    }

    [Fact]
    public void Death_Respawn_Reentry_Baseline_Uses_New_Life_And_Not_Old_Equipment()
    {
        // 死亡→复活（LifeId++）后再次进入视野：新基线必须是新生命代次，
        // 旧生命的状态/装备不得被旧增量复活。
        var catalog = ElementalCatalogDefaults.Create();
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        MoveTo(observer, 0f);
        var target = store.AddPlayer(2, "Target");
        MoveTo(target, 50f);
        target.ConfigureStatuses(catalog);
        target.EquippedWeaponInstanceId = 42;
        target.EquippedWeaponTemplateId = ElementalCatalogDefaults.IceSwordTemplateId;
        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        target.Statuses!.Apply(burn, sourceId: 1, skillId: 10002, castId: 7, tick: 0);
        target.ApplyDamage(10f); // Hp 90

        var broadcaster = NewBroadcaster(catalog);
        var capture = CaptureOf(broadcaster);
        uint tick = 1;

        Assert.Single(broadcaster.BuildForObserver(observer, tick++, store));
        var oldLife = capture.Last.Combat!;
        Assert.Equal(1u, oldLife.LifeId);
        Assert.Equal(42L, oldLife.Equipment!.WeaponInstanceId);
        Assert.Contains(201, oldLife.Statuses.Select(s => s.EffectId));
        Assert.Equal(90f, oldLife.Hp, 4);
        uint versionAtBaseline = oldLife.CombatVersion;

        // 死亡：旧生命最后一条增量（Dead/Health 翻转）仍带旧 LifeId，此后不得再投递。
        target.ApplyDamage(200f);
        target.MarkDead(100);
        Assert.False(target.IsAlive);
        Assert.False(target.TryRespawn(221, 66)); // 8s 前一个 tick：不复活
        Assert.Equal(1u, target.LifeId);
        broadcaster.BuildForObserver(observer, tick++, store);
        var death = capture.Last;
        Assert.True(death.Dead);
        Assert.True((death.FieldMask & (ushort)EntityFieldMask.Health) != 0);
        Assert.Equal(1u, death.LifeId);
        Assert.True(death.CombatVersion > versionAtBaseline);
        Assert.Null(death.Combat); // 增量帧不带完整投影（投影走基线与 CombatStateUpdate 可靠更新）

        // 离开视野后复活并换装：基线必须重建为当前生命，而不是旧生命 last-known。
        MoveTo(target, 91f);
        broadcaster.BuildForObserver(observer, tick++, store);
        int frozen = capture.Sent.Count;

        Assert.True(target.TryRespawn(222, 66)); // (222-100)*66ms ≥ 8s
        Assert.True(target.IsAlive);
        Assert.Equal(2u, target.LifeId);
        Assert.Equal(target.MaxHp, target.Hp, 4);
        Assert.Empty(target.Statuses!.Runtime.Active);
        target.EquippedWeaponInstanceId = 99;
        target.EquippedWeaponTemplateId = ElementalCatalogDefaults.FireSwordTemplateId;

        broadcaster.BuildForObserver(observer, tick++, store);
        broadcaster.BuildForObserver(observer, tick++, store);
        Assert.Equal(frozen, capture.Sent.Count); // 视野外复活 = 零投递

        MoveTo(target, 50f);
        broadcaster.BuildForObserver(observer, tick++, store);
        Assert.Equal(frozen + 1, capture.Sent.Count);
        var reborn = capture.Last;
        AssertBaselineMatchesServer(reborn, target, catalog);
        Assert.Equal(2u, reborn.Combat!.LifeId);
        Assert.Equal(2u, reborn.LifeId);
        Assert.Equal(99L, reborn.Combat.Equipment!.WeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.FireSwordTemplateId,
            reborn.Combat.Equipment.TemplateId);
        Assert.Equal(2, reborn.Combat.Equipment.ElementId);
        Assert.Empty(reborn.Combat.Statuses);
        Assert.True(reborn.Combat.CombatVersion > death.CombatVersion);

        // 退出后（frozen 之后）送达的每一条都只能是新生命；旧 LifeId 不得"复活"。
        Assert.DoesNotContain(capture.Sent.Skip(frozen), s => s.LifeId == 1);
        Assert.Equal(3, capture.Sent.Count); // 首帧基线 + 死亡增量 + 重见基线
    }

    [Fact]
    public void Evade_Home_Reentry_Baseline_Matches_Healed_Server_State()
    {
        // 怪物 Evade 回巢到家：回满血 + 清状态 + CombatVersion bump。
        // 视野外往返后的重见基线必须携带回巢后的最终状态，版本单调不回退。
        var catalog = ElementalCatalogDefaults.Create();
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        MoveTo(observer, 0f);

        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        module.OnMonsterSpawned = entity => entity.ConfigureStatuses(catalog);
        long npcId = module.SpawnMonster(1, new Vector3(0f, 0f, 50f),
            patrolCenter: new Vector3(0f, 0f, 50f));
        var npc = store.GetPlayer(npcId)!;
        var state = module.GetState(npcId)!;

        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        npc.Statuses!.Apply(burn, sourceId: 1, skillId: 10002, castId: 7, tick: 0);
        npc.ApplyDamage(40f);
        state.Mode = MonsterMode.Evade;
        state.EvadeStartTick = 0;

        var broadcaster = NewBroadcaster(catalog);
        var capture = CaptureOf(broadcaster);
        uint tick = 1;
        Assert.Single(broadcaster.BuildForObserver(observer, tick++, store)); // 带灼烧/掉血的基线
        var before = capture.Last.Combat!;
        Assert.Contains(201, before.Statuses.Select(s => s.EffectId));
        Assert.Equal(npc.Hp, before.Hp, 4);
        uint versionBefore = before.CombatVersion;

        module.Tick(1); // 已在 SpawnPosition → ArriveHome
        Assert.Equal(MonsterMode.Patrol, state.Mode);
        Assert.Equal(npc.MaxHp, npc.Hp, 4);
        Assert.Empty(npc.Statuses!.Runtime.Active);
        Assert.True(npc.CombatVersion > versionBefore, "ArriveHome 必须 bump CombatVersion");
        uint versionAfter = npc.CombatVersion;

        // 观察者离场再回来：重见基线必须与服务器最终状态一致。
        observer.UpdatePosition(new Vector3(200f, 0f, 0f), 0f, 0f, 2L, 1U);
        broadcaster.BuildForObserver(observer, tick++, store);
        int frozen = capture.Sent.Count;
        broadcaster.BuildForObserver(observer, tick++, store);
        Assert.Equal(frozen, capture.Sent.Count); // 视野外零投递

        MoveTo(observer, 0f);
        broadcaster.BuildForObserver(observer, tick++, store);
        Assert.Equal(frozen + 1, capture.Sent.Count);
        var reentry = capture.Last;
        Assert.Equal(npcId, reentry.PlayerId);
        AssertBaselineMatchesServer(reentry, npc, catalog);
        Assert.Empty(reentry.Combat!.Statuses);
        Assert.Equal(versionAfter, reentry.Combat.CombatVersion);
        Assert.True(reentry.Combat.CombatVersion > versionBefore);
        Assert.Equal(2, capture.Sent.Count); // 首帧基线 + 重见基线；视野外零增量
    }

    [Fact]
    public void Evade_Pooled_Respawn_Starts_New_Entity_And_Old_Id_Stops()
    {
        // 回巢超时 → despawn → 30 tick 后回池以**新 id** 重生。
        // 本用例锁定当前生产语义（revised brief 任务 4 第 3 条）：新实体 LifeId=1、
        // CombatVersion=0；**不承诺跨实体 id 的 CombatVersion 单调**（客户端按实体 id
        // 重建视图）。旧 id 必须立即停止投递，新 id 首帧是完整基线。
        var catalog = ElementalCatalogDefaults.Create();
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        MoveTo(observer, 0f);

        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        module.OnMonsterSpawned = entity => entity.ConfigureStatuses(catalog);
        long oldId = module.SpawnMonster(1, new Vector3(0f, 0f, 50f),
            patrolCenter: new Vector3(0f, 0f, 50f));
        var oldNpc = store.GetPlayer(oldId)!;
        var state = module.GetState(oldId)!;

        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        oldNpc.Statuses!.Apply(burn, sourceId: 1, skillId: 10002, castId: 7, tick: 0);
        oldNpc.ApplyDamage(40f);

        var broadcaster = NewBroadcaster(catalog);
        var capture = CaptureOf(broadcaster);
        Assert.Single(broadcaster.BuildForObserver(observer, tick: 1, store));
        var oldBaseline = capture.Last;
        Assert.Equal(oldId, oldBaseline.PlayerId);
        Assert.Contains(201, oldBaseline.Combat!.Statuses.Select(s => s.EffectId));

        // 回不到家 → ReturnTimeoutTicks 超时 despawn，EvadeRespawnDelayTicks 后回池重生于巡逻中心。
        state.Mode = MonsterMode.Evade;
        state.EvadeStartTick = 0;
        state.SpawnPosition = new Vector3(0f, 0f, 1000f);
        for (uint t = 0; t <= MonsterBrain.ReturnTimeoutTicks; t++) module.Tick(t);
        Assert.Null(store.GetPlayer(oldId));
        Assert.Contains(oldId, module.DrainRemovedNpcs());
        Assert.Equal(0, module.Count);

        broadcaster.RemoveEntity(oldId); // Server3C.DrainRemovedNpcs 的 AOI 清理接线
        int frozen = capture.Sent.Count;
        uint removedTick = MonsterBrain.ReturnTimeoutTicks;
        broadcaster.BuildForObserver(observer, tick: removedTick + 1, store);
        Assert.Equal(frozen, capture.Sent.Count); // 旧 id 已注销：不得再发

        uint respawnTick = removedTick + MonsterModule.EvadeRespawnDelayTicks;
        for (uint t = removedTick + 1; t < respawnTick; t++)
        {
            module.Tick(t);
            Assert.Equal(0, module.Count);
        }
        module.Tick(respawnTick);
        Assert.Equal(1, module.Count);

        var newNpc = Assert.Single(store.All, p => p.Kind == (byte)EntityKind.Npc);
        long newId = newNpc.PlayerId;
        Assert.NotEqual(oldId, newId);
        Assert.Equal(1u, newNpc.LifeId); // 新生成实体 = 新生命（生产语义）
        Assert.Equal(0u, newNpc.CombatVersion); // 新实体 CV 从 0 开始（不承诺跨 id 单调）
        Assert.Equal(newNpc.MaxHp, newNpc.Hp, 4);
        Assert.Empty(newNpc.Statuses!.Runtime.Active);

        broadcaster.BuildForObserver(observer, tick: respawnTick + 1, store);
        Assert.Equal(frozen + 1, capture.Sent.Count);
        var reborn = capture.Last;
        Assert.Equal(newId, reborn.PlayerId);
        AssertBaselineMatchesServer(reborn, newNpc, catalog);
        Assert.Empty(reborn.Combat!.Statuses);
        Assert.Null(reborn.Combat.Equipment);

        // 旧 id 的旧状态只是历史基线；注销后不得有任何残留增量。
        Assert.DoesNotContain(capture.Sent.Skip(frozen), s => s.PlayerId == oldId);
        Assert.Equal(2, capture.Sent.Count); // 旧 id 首帧基线 + 新 id 首帧基线
    }

    [Fact]
    public void Observer_Reconnect_First_Baseline_Is_Full_Projection_Without_Stale_State()
    {
        // O 断线（RemoveObserver 清视野/节拍）→ 重进：目标处于旧迟滞带不得凭残留状态可见；
        // 新会话首条基线必须是当前完整投影，不携带断线前的状态/装备。
        var catalog = ElementalCatalogDefaults.Create();
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        MoveTo(observer, 0f);
        var target = store.AddPlayer(2, "Target");
        MoveTo(target, 50f);
        target.ConfigureStatuses(catalog);
        target.EquippedWeaponInstanceId = 42;
        target.EquippedWeaponTemplateId = ElementalCatalogDefaults.IceSwordTemplateId;
        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        target.Statuses!.Apply(burn, sourceId: 1, skillId: 10002, castId: 7, tick: 0);

        var broadcaster = NewBroadcaster(catalog);
        var capture = CaptureOf(broadcaster);
        Assert.Single(broadcaster.BuildForObserver(observer, tick: 1, store));
        var oldView = capture.Last.Combat!;
        Assert.Equal(42L, oldView.Equipment!.WeaponInstanceId);
        Assert.Contains(201, oldView.Statuses.Select(s => s.EffectId));

        // 断线：Server3C.OnSessionDisconnected → RemoveObserver
        broadcaster.RemoveObserver(observer.PlayerId);
        Assert.False(broadcaster.HasSightState);

        // 断线期间权威状态推进：清状态、换装、掉血。
        target.Statuses.ClearAll(2);
        target.EquippedWeaponInstanceId = 99;
        target.EquippedWeaponTemplateId = ElementalCatalogDefaults.FireSwordTemplateId;
        target.ApplyDamage(10f);

        // 新会话第一帧目标在 85m（旧迟滞带）：残留 seen=true 会错误重见。
        MoveTo(target, 85f);
        broadcaster.BuildForObserver(observer, tick: 2, store);
        Assert.Single(capture.Sent);
        Assert.Single(capture.Baselines);

        // 进入 ≤80m：新会话首条基线 = 当前完整投影。
        MoveTo(target, 80f);
        broadcaster.BuildForObserver(observer, tick: 3, store);
        Assert.Equal(2, capture.Sent.Count);
        Assert.Equal(2, capture.Baselines.Count);
        AssertBaselineMatchesServer(capture.Last, target, catalog);
        var fresh = capture.Last.Combat!;
        Assert.Equal(99L, fresh.Equipment!.WeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.FireSwordTemplateId, fresh.Equipment.TemplateId);
        Assert.Equal(2, fresh.Equipment.ElementId);
        Assert.Empty(fresh.Statuses);
        Assert.True(fresh.CombatVersion > oldView.CombatVersion,
            "断线期间状态推进后，新会话基线版本必须严格大于旧视图");
    }

    [Fact]
    public void Old_Life_CombatStateUpdate_Stops_At_Exit_And_New_Life_Update_Follows_Reentry()
    {
        // CombatStateUpdate 可靠增量走 AOI 收件人：先在同一视野内**真实构造**旧生命
        // （LifeId=1）的存活与死亡两条 CombatStateUpdate，再出界推进旧生命并断言零投递；
        // 复活（LifeId=2）重进后，下一条可靠增量必须是新生命/新装备。
        using var server = new Server3C(new TestLogger());
        var sent = new List<(long SessionId, MessageId Id, IMessage Message)>();
        server.OfflineSendSink = (session, id, message) => sent.Add((session.SessionId, id, message));

        var observer = server.RegisterEntity(1, "Obs");
        observer.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 0L, 0U);
        var target = server.RegisterEntity(2, "Target");
        target.UpdatePosition(new Vector3(50f, 0f, 0f), 0f, 0f, 0L, 0U);
        server.BindPlayerToSession(1, new FakeSession(11));

        // 正面控制 1：视野内存活且变脏 → 观察者收到 LifeId=1 的 CombatStateUpdate。
        target.ApplyDamage(10f);
        server.GetVisiblePlayers(1);
        server.SendCombatStateUpdates();
        var first = Assert.Single(sent, m => m.Id == MessageId.CombatStateUpdate);
        Assert.Equal(11L, first.SessionId); // 收件人 = 观察者会话
        var aliveUpdate = (CombatStateUpdate)first.Message;
        Assert.Equal(1u, aliveUpdate.Projection.LifeId);
        Assert.Equal(90f, aliveUpdate.Projection.Hp, 4);
        Assert.False(aliveUpdate.Projection.Dead);
        sent.Clear();

        // 正面控制 2：视野内死亡仍变脏 → 旧生命的死亡投影也必须真实投递过一条。
        target.ApplyDamage(200f);
        target.MarkDead(100);
        Assert.False(target.IsAlive);
        server.SendCombatStateUpdates();
        var death = Assert.Single(sent, m => m.Id == MessageId.CombatStateUpdate);
        Assert.Equal(11L, death.SessionId);
        var deathUpdate = (CombatStateUpdate)death.Message;
        Assert.Equal(1u, deathUpdate.Projection.LifeId);
        Assert.True(deathUpdate.Projection.Dead);
        Assert.Equal(-110f, deathUpdate.Projection.Hp, 4);
        sent.Clear();

        // 出界后旧生命继续推进：新的旧-LifeId 脏状态不得投递给观察者。
        target.UpdatePosition(new Vector3(91f, 0f, 0f), 0f, 0f, 1L, 1U);
        server.GetVisiblePlayers(1); // 观察者侧 AOI 刷新：目标出界
        target.BumpCombatVersion(); // 旧生命脏位（若仍收件，会产生 LifeId=1 的增量）
        Assert.True(target.CombatDirty);
        server.SendCombatStateUpdates();
        Assert.Empty(sent); // 旧生命 CombatStateUpdate 在出界后停止

        // 视野外复活（LifeId++）→ 换装；重进后可靠增量必须是新生命/新装备。
        Assert.True(target.TryRespawn(222, 66)); // (222-100)*66ms ≥ 8s
        Assert.Equal(2u, target.LifeId);
        target.EquippedWeaponInstanceId = 99;
        target.EquippedWeaponTemplateId = ElementalCatalogDefaults.FireSwordTemplateId;
        target.UpdatePosition(new Vector3(50f, 0f, 0f), 0f, 0f, 2L, 2U);
        server.GetVisiblePlayers(1);
        target.ApplyDamage(1f); // Hp 100 -> 99，新生命脏位
        server.SendCombatStateUpdates();
        var update = Assert.Single(sent, m => m.Id == MessageId.CombatStateUpdate);
        Assert.Equal(11L, update.SessionId);
        var reborn = (CombatStateUpdate)update.Message;
        Assert.Equal(2u, reborn.Projection.LifeId);
        Assert.Equal(99f, reborn.Projection.Hp, 4);
        Assert.Equal(99L, reborn.Projection.Equipment!.WeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.FireSwordTemplateId, reborn.Projection.Equipment.TemplateId);
    }

    private static AoiBroadcaster NewBroadcaster(ElementalCatalogDefinition catalog)
        => new AoiBroadcaster
        {
            CombatProjectionProvider = entity => CombatProjectionBuilder.Build(entity, catalog)
        };

    private sealed class Capture
    {
        public readonly List<EntitySnapshot> Sent = new();
        public readonly List<(long ObserverId, long EntityId)> Baselines = new();
        public EntitySnapshot Last => Sent[Sent.Count - 1];
    }

    private static Capture CaptureOf(AoiBroadcaster broadcaster)
    {
        var capture = new Capture();
        broadcaster.OnEntityBroadcast = (_, snapshot) => capture.Sent.Add(snapshot);
        broadcaster.OnBaselineEntity = (observerId, entityId) =>
            capture.Baselines.Add((observerId, entityId));
        return capture;
    }

    private static void MoveTo(PlayerState entity, float x)
        => entity.UpdatePosition(new Vector3(x, 0f, 0f), 0f, 0f, 1L, 0U);

    private static bool IsVisible(AoiBroadcaster broadcaster, EntityStore store,
        PlayerState observer, PlayerState entity)
        => broadcaster.GetVisible(observer.PlayerId, store)
            .Any(e => e.PlayerId == entity.PlayerId);

    /// <summary>
    /// 基线快照必须与服务器实体当前状态逐项一致：Combat 位 + 完整投影，
    /// LifeId/CombatVersion/Hp/Statuses/EquipmentSummary 全部取当前值。
    /// </summary>
    private static void AssertBaselineMatchesServer(EntitySnapshot snapshot, PlayerState server,
        ElementalCatalogDefinition catalog)
    {
        Assert.True((snapshot.FieldMask & (ushort)EntityFieldMask.Combat) != 0,
            "基线快照必须置位 Combat 位");
        Assert.True((snapshot.FieldMask & (ushort)EntityFieldMask.Health) != 0,
            "基线快照必须携带 Health");
        Assert.NotNull(snapshot.Combat);
        var projection = snapshot.Combat!;

        Assert.Equal(server.LifeId, snapshot.LifeId);
        Assert.Equal(server.CombatVersion, snapshot.CombatVersion);
        Assert.Equal(server.LifeId, projection.LifeId);
        Assert.Equal(server.CombatVersion, projection.CombatVersion);
        Assert.Equal(server.Hp, snapshot.Hp);
        Assert.Equal(server.Hp, projection.Hp);
        Assert.Equal(server.MaxHp, projection.MaxHp);
        Assert.Equal(!server.IsAlive, projection.Dead);
        Assert.Equal(server.IsDefending, projection.IsDefending);

        // 状态列表逐项对齐（不只 EffectId）：组别/结束 tick/来源也必须是服务器当前值。
        var expectedStatuses = server.Statuses == null
            ? new List<(int EffectId, int GroupId, uint EndTick, long SourceId)>()
            : server.Statuses.Runtime.Active
                .Select(s => (s.EffectId, s.GroupId, s.EndTick, s.SourceId))
                .OrderBy(s => s.EffectId)
                .ToList();
        var actualStatuses = projection.Statuses
            .Select(s => (s.EffectId, s.GroupId, s.EndTick, s.SourceId))
            .OrderBy(s => s.EffectId)
            .ToList();
        Assert.Equal(expectedStatuses, actualStatuses);

        if (server.EquippedWeaponInstanceId == 0)
        {
            Assert.Null(projection.Equipment);
            return;
        }

        Assert.NotNull(projection.Equipment);
        var weapon = catalog.Weapons.Find(w => w.TemplateId == server.EquippedWeaponTemplateId);
        Assert.Equal(server.EquippedWeaponInstanceId, projection.Equipment!.WeaponInstanceId);
        Assert.Equal(server.EquippedWeaponTemplateId, projection.Equipment.TemplateId);
        Assert.Equal(weapon?.ElementId ?? 0, projection.Equipment.ElementId);
    }

    private sealed class FakeSession : IServerSession
    {
        public FakeSession(long sessionId) => SessionId = sessionId;
        public long SessionId { get; }
        public bool IsConnected => true;
        public DateTime LastActivityTime => DateTime.UtcNow;
        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? Error;
        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable) { }
        public void UpdateActivity() { }
        public bool IsTimedOut(TimeSpan timeout) => false;
        public Task CloseAsync(string reason = "Normal") => Task.CompletedTask;
        public void Dispose() { }
    }
}

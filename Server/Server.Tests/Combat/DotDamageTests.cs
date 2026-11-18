using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>Plan 04 任务 4：灼烧跳数、`DamageResult` 发布、死亡/复活/Evade 清理。
/// `StatusTickService` 是主循环 preTick 的权威入口；本文件直调 `Tick`（不启动 GameLoop）。</summary>
public class DotDamageTests
{
    private const int FireSkillId = 10002;
    private const ulong BurnCastId = 7UL;

    internal static ElementalCatalogDefinition NewCatalog() => ElementalCatalogDefaults.Create();

    internal static StatusEffectDefinition BurnOf(ElementalCatalogDefinition catalog)
        => catalog.Statuses.Find(s => s.EffectId == 201)!;

    internal static StatusEffectDefinition WindOf(ElementalCatalogDefinition catalog)
        => catalog.Statuses.Find(s => s.EffectId == 401)!;

    internal static void Attach(PlayerState target, ElementalCatalogDefinition catalog,
        StatusEffectDefinition def, long sourceId, uint tick, ulong castId = BurnCastId)
    {
        target.ConfigureStatuses(catalog);
        var result = target.Statuses!.Apply(def, sourceId, FireSkillId, castId, tick, catalog);
        Assert.NotEqual(StatusApplyResult.Ignored, result);
    }

    [Fact]
    public void Burn_Ticks_On_Interval_And_Publishes_Dot_Hit_Identity()
    {
        using var h = new DotTestHarness();
        var catalog = h.Catalog;
        var target = h.Store.AddPlayer(2, "Target");
        target.SetMaxHp(100f);
        Attach(target, catalog, BurnOf(catalog), sourceId: 1, tick: 0);
        h.Bind(target.PlayerId, sessionId: 102);

        var service = h.NewService();

        service.Tick(14);
        Assert.Empty(h.DotHits()); // 首次跳在 tick 15

        uint before = target.CombatVersion;
        service.Tick(15);
        var first = Assert.Single(h.DotHits());
        Assert.Equal(DamageKind.Dot, first.DamageKind);
        Assert.Equal(2L, first.TargetPlayerId);
        Assert.Equal(BurnCastId, first.CastId);
        Assert.Equal(target.LifeId, first.TargetLifeId);
        Assert.Equal(8f, first.Damage, 3);                 // 火焰每跳 8；无风
        Assert.Equal(92f, first.TargetRemainingHp, 3);
        Assert.Equal(target.Hp, first.TargetRemainingHp, 3);
        Assert.Equal(1u, first.EventSeq);
        Assert.Equal(1UL, first.EventId);
        Assert.Equal(before + 1u, first.CombatVersion);    // 扣血 bump

        before = target.CombatVersion;
        service.Tick(30);
        Assert.Equal(2, h.DotHits().Count);
        var second = h.DotHits()[1];
        Assert.Equal(2u, second.EventSeq);
        Assert.Equal(2UL, second.EventId);
        Assert.Equal(84f, second.TargetRemainingHp, 3);
        Assert.Equal(before + 1u, second.CombatVersion);

        before = target.CombatVersion;
        service.Tick(45);                                  // EndTick 排定末跳
        Assert.Equal(3, h.DotHits().Count);
        var third = h.DotHits()[2];
        Assert.Equal(3u, third.EventSeq);
        Assert.Equal(3UL, third.EventId);
        Assert.Equal(76f, third.TargetRemainingHp, 3);
        Assert.Equal(before + 1u, third.CombatVersion);    // 末跳扣血
        Assert.Equal(before + 2u, target.CombatVersion);   // 末跳移除状态再 bump
        Assert.Equal(0, target.Statuses!.Runtime.Count);

        service.Tick(46);
        Assert.Equal(3, h.DotHits().Count);                // 移除后不再跳
    }

    [Fact]
    public void Burn_Tick_That_Kills_Player_Clears_Statuses_And_Stops()
    {
        using var h = new DotTestHarness();
        var catalog = h.Catalog;
        var target = h.Store.AddPlayer(2, "Target");
        target.SetMaxHp(8f);                               // 一跳（8）即死
        Attach(target, catalog, BurnOf(catalog), sourceId: 1, tick: 0);
        h.Bind(target.PlayerId, sessionId: 102);

        var service = h.NewService();
        service.Tick(15);

        Assert.False(target.IsAlive);
        Assert.Equal(0f, target.Hp, 3);
        Assert.Single(h.DotHits());
        Assert.Equal(0f, h.DotHits()[0].TargetRemainingHp, 3);
        Assert.Empty(target.Statuses!.Runtime.Active);      // 死亡立即清状态

        service.Tick(30);
        Assert.Single(h.DotHits());                        // 死后不再跳
        Assert.Empty(target.Statuses!.Runtime.Active);
    }

    [Fact]
    public void Monster_Dot_Uses_OnDamageTaken_Without_Hit_Reaction_And_Threat_By_Damage()
    {
        using var h = new DotTestHarness(monsters: true);
        var catalog = h.Catalog;
        var attacker = h.Store.AddPlayer(1, "Attacker");
        attacker.IsLoggedIn = true;
        attacker.UpdatePosition(new Vector3(0f, 0f, -3f), 0f, 0f, 1L, 0U);

        long npcId = h.Monsters!.SpawnMonster(1, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
        var npc = h.Store.GetPlayer(npcId)!;
        var state = h.Monsters.GetState(npcId)!;
        Attach(npc, catalog, BurnOf(catalog), sourceId: 1, tick: 0);
        h.Bind(1, sessionId: 101);

        h.NewService().Tick(15);

        Assert.Equal(8f * ThreatTable.ThreatPerDamage, state.Threats.ThreatOf(1), 3); // 不叠加 ThreatFlat
        Assert.Null(state.HitStartTick);
        Assert.NotEqual((byte)NpcAnimationState.Hit, npc.ActionState);

        // 主循环随后推进怪物 tick：DoT 已更新 LastHp，不得再被 DetectHitOrDeath 识别为受击。
        h.Monsters.Tick(15);
        Assert.Null(state.HitStartTick);
        Assert.NotEqual((byte)NpcAnimationState.Hit, npc.ActionState);
        Assert.Equal(npc.Hp, state.LastHp, 3);
    }

    [Fact]
    public void Player_MarkDead_Clears_Statuses()
    {
        var catalog = NewCatalog();
        var player = new PlayerState(2);
        player.SetMaxHp(100f);
        player.ConfigureStatuses(catalog);
        Attach(player, catalog, WindOf(catalog), sourceId: 1, tick: 0);
        player.ApplyDamage(100f);

        player.MarkDead(tick: 10);

        Assert.False(player.IsAlive);
        Assert.Empty(player.Statuses!.Runtime.Active);
        Assert.Equal(1f, player.ControlSpeedScale, 3);
        Assert.False(player.ControlActionLock);
    }

    [Fact]
    public void Player_TryRespawn_Clears_Statuses()
    {
        var catalog = NewCatalog();
        var player = new PlayerState(2);
        player.SetMaxHp(100f);
        player.ConfigureStatuses(catalog);
        player.ApplyDamage(100f);
        player.MarkDead(tick: 10);                          // DeadTick=0 是“未死亡”哨兵
        Assert.Empty(player.Statuses!.Runtime.Active);

        // MarkDead 已清空——复活路径自身的清理用死后再次写入的状态验证。
        Attach(player, catalog, WindOf(catalog), sourceId: 1, tick: 11);
        Assert.Single(player.Statuses.Runtime.Active);

        Assert.True(player.TryRespawn(nowTick: 132, tickMs: Server3C.TickIntervalMs)); // 122 tick × 66ms = 8052ms ≥ 8s

        Assert.True(player.IsAlive);
        Assert.Equal(player.MaxHp, player.Hp, 3);
        Assert.Equal(2u, player.LifeId);
        Assert.Empty(player.Statuses.Runtime.Active);
    }

    [Fact]
    public void Monster_Evade_ArriveHome_Clears_Statuses_And_Heals()
    {
        var catalog = NewCatalog();
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long id = module.SpawnMonster(1, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
        var entity = store.GetPlayer(id)!;
        var state = module.GetState(id)!;
        Attach(entity, catalog, WindOf(catalog), sourceId: 1, tick: 0);

        state.Mode = MonsterMode.Combat;
        state.TargetId = null;
        state.Position = new Vector3(0f, 0f, 25f);         // > Slime leash 23

        uint t = 0;
        while (t < 600 && state.Mode != MonsterMode.Patrol) { module.Tick(t); t++; }

        Assert.Equal(MonsterMode.Patrol, state.Mode);
        Assert.Empty(entity.Statuses!.Runtime.Active);
        Assert.Equal(entity.MaxHp, entity.Hp, 3);
    }

    [Fact]
    public async Task Dot_DamageResult_Recipients_Match_Direct_Damage_Rule()
    {
        using var server = new Server3C(new TestLogger());
        var sent = new List<(long SessionId, MessageId Id, IMessage Message)>();
        server.OfflineSendSink = (session, id, message) => sent.Add((session.SessionId, id, message));

        var source = server.RegisterEntity(1, "Source");
        source.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);
        var target = server.RegisterEntity(2, "Target");
        target.UpdatePosition(new Vector3(0f, 0f, 2f), 0f, 0f, 1L, 0U);
        var observer = server.RegisterEntity(3, "Observer");
        observer.UpdatePosition(new Vector3(0f, 0f, 3f), 0f, 0f, 1L, 0U);
        var far = server.RegisterEntity(4, "Far");
        far.UpdatePosition(new Vector3(0f, 0f, 5000f), 0f, 0f, 1L, 0U);

        server.BindPlayerToSession(1, new DotFakeSession(101));
        server.BindPlayerToSession(2, new DotFakeSession(102));
        server.BindPlayerToSession(3, new DotFakeSession(103));
        server.BindPlayerToSession(4, new DotFakeSession(104));

        var cast = new CastHandler(server, new TestLogger(),
            new CombatResolver(new EntityStore(), new Random(1)));
        var hit = new HitEntry
        {
            TargetPlayerId = 2,
            Damage = 8f,
            DamageKind = DamageKind.Dot,
            CastId = BurnCastId,
            TargetLifeId = target.LifeId,
            TargetRemainingHp = 92f,
            EventSeq = 1,
            EventId = 1
        };

        await cast.BroadcastDotDamageAsync(sourceId: 1, skillId: FireSkillId, hit);

        var bySession = sent.Where(m => m.Id == MessageId.DamageResult)
            .GroupBy(m => m.SessionId).ToDictionary(g => g.Key, g => g.ToList());
        Assert.Equal(new long[] { 101L, 102L, 103L }, bySession.Keys.OrderBy(id => id).ToArray());
        Assert.DoesNotContain(104L, bySession.Keys);

        foreach (var message in bySession.Values.SelectMany(v => v))
        {
            var result = Assert.IsType<DamageResult>(message.Message);
            Assert.Equal(1L, result.SourcePlayerId);
            Assert.Equal(FireSkillId, result.SkillId);
            Assert.Equal((int)EntityKind.Player, result.SourceKind);
            Assert.NotEqual(0L, result.ServerTimestamp);
            Assert.Same(hit, Assert.Single(result.Hits));
        }
    }
}

/// <summary>任务 4 共用夹具：独立 EntityStore + 真实 CastHandler（下行经 OfflineSendSink 观察）。</summary>
internal sealed class DotTestHarness : IDisposable
{
    public readonly ElementalCatalogDefinition Catalog = DotDamageTests.NewCatalog();
    public readonly EntityStore Store = new();
    public readonly Server3C Server;
    public readonly CastHandler Cast;
    public readonly List<(long SessionId, MessageId Id, IMessage Message)> Sent = new();
    public MonsterModule? Monsters;

    public DotTestHarness(bool monsters = false)
    {
        Server = new Server3C(new TestLogger());
        Server.OfflineSendSink = (session, id, message) => Sent.Add((session.SessionId, id, message));
        Cast = new CastHandler(Server, new TestLogger(),
            new CombatResolver(Store, new Random(1), Catalog));
        if (monsters)
        {
            long seed = MonsterModule.NpcIdSeed;
            Monsters = new MonsterModule(Store, () => ++seed, actionV1: true);
        }
    }

    public StatusTickService NewService() => new(Store, Cast, () => Monsters, Catalog);

    public void Bind(long playerId, long sessionId)
        => Server.BindPlayerToSession(playerId, new DotFakeSession(sessionId));

    public List<HitEntry> DotHits()
        => Sent.Where(s => s.Id == MessageId.DamageResult)
            .SelectMany(s => ((DamageResult)s.Message).Hits)
            .Where(h => h.DamageKind == DamageKind.Dot)
            .GroupBy(h => h.EventId)
            .Select(g => g.First())
            .OrderBy(h => h.EventSeq)
            .ToList();

    public void Dispose() => Server.Dispose();
}

internal sealed class DotFakeSession : IServerSession
{
    public DotFakeSession(long sessionId) => SessionId = sessionId;
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

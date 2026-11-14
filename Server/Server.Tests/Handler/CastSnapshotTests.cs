using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CastSnapshotTests
{
    private static CastRequest Basic(int skillId = 10001, uint seq = 0, uint revision = 0,
        uint lifeId = 0)
        => new CastRequest
        {
            SkillId = skillId,
            ClientCastSeq = seq,
            EquipmentRevision = revision,
            CasterLifeId = lifeId,
            Shape = new HitShape
            {
                ShapeType = 0,
                Range = skillId == 10001 ? 2f : 2.5f,
                Angle = 120f
            }
        };

    private static PlayerState AddCaster(EntityStore store, long id = 1L)
    {
        var player = store.AddPlayer(id, "P" + id);
        player.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 0L, 0U);
        return player;
    }

    private static void Equip(PlayerState player, long instanceId, int templateId, uint revision)
    {
        player.WeaponInstanceTemplates[instanceId] = templateId;
        player.EquippedWeaponInstanceId = instanceId;
        player.EquippedWeaponTemplateId = templateId;
        player.EquipmentRevision = revision;
    }

    private static ElementalCatalogDefinition Catalog(float iceAttack, float iceSpeed,
        float iceQiMultiplier, float fireAttack, float fireSpeed)
    {
        var catalog = ElementalCatalogDefaults.Create();
        var ice = catalog.Weapons.Find(w => w.TemplateId == ElementalCatalogDefaults.IceSwordTemplateId);
        ice!.BaseAttack = iceAttack;
        ice.AttackSpeedMultiplier = iceSpeed;
        ice.SwordQiDamageMultiplier = iceQiMultiplier;
        var fire = catalog.Weapons.Find(w => w.TemplateId == ElementalCatalogDefaults.FireSwordTemplateId);
        fire!.BaseAttack = fireAttack;
        fire.AttackSpeedMultiplier = fireSpeed;
        return catalog;
    }

    private static List<CombatResolver.CastResolution> DrainTo(CombatResolver resolver, uint tick)
    {
        var all = new List<CombatResolver.CastResolution>();
        for (uint t = 1; t <= tick; t++) all.AddRange(resolver.DrainResolved(t));
        return all;
    }

    private static bool MatchesBaseDamage(float actual, float expectedBase)
        => Math.Abs(actual - expectedBase) < 0.01f
           || Math.Abs(actual - expectedBase * CombatRules.CritMultiplier) < 0.01f;

    [Fact]
    public void Decide_Allocates_Monotonic_CastId()
    {
        var store = new EntityStore();
        AddCaster(store);
        var resolver = new CombatResolver(store);

        var a = resolver.Decide(1, Basic(), 100);
        var b = resolver.Decide(1, Basic(), 200);

        Assert.True(a.Accepted);
        Assert.True(b.Accepted);
        Assert.True(a.Confirm!.CastId > 0);
        Assert.True(b.Confirm!.CastId > a.Confirm.CastId);
    }

    [Fact]
    public void Broadcast_Carries_Weapon_Snapshot()
    {
        var store = new EntityStore();
        var player = AddCaster(store);
        Equip(player, 10, ElementalCatalogDefaults.IceSwordTemplateId, 2);

        var resolver = new CombatResolver(store);
        var result = resolver.Decide(1, new CastRequest
        {
            SkillId = 10002,
            ClientCastSeq = 1,
            EquipmentRevision = 2,
            CasterLifeId = player.LifeId,
            Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f }
        }, 100);

        Assert.True(result.Accepted);
        Assert.Equal(2u, result.Broadcast!.EquipmentRevision);
        Assert.Equal(1, result.Broadcast.ElementId);
        Assert.Equal(1f, result.Broadcast.AttackSpeedMultiplier);
        Assert.Equal(result.Confirm!.CastId, result.Broadcast.CastId);
    }

    [Fact]
    public void AttackSpeed_Changes_First_Hit_And_Next_Allowed_Tick()
    {
        var store = new EntityStore();
        var player = AddCaster(store);
        Equip(player, 10, ElementalCatalogDefaults.IceSwordTemplateId, 0);
        store.AddPlayer(2, "target").UpdatePosition(new Vector3(0f, 0f, 1.5f), 0f, 0f, 0L, 0U);

        var catalog = Catalog(iceAttack: 1.5f, iceSpeed: 1.25f, iceQiMultiplier: 1f,
            fireAttack: 0.75f, fireSpeed: 0.8f);
        var resolver = new CombatResolver(store, new Random(1), catalog);

        var accepted = resolver.Decide(1, Basic(10002, seq: 1, revision: 0, lifeId: player.LifeId), 0);
        Assert.True(accepted.Accepted);

        // 0.35s / 1.25 = 0.28s → ceil(4.2) = 5 tick；第 5 tick 才应结算挥击。
        Assert.Empty(resolver.DrainResolved(4));
        var firstStage = Assert.Single(resolver.DrainResolved(5));
        Assert.Equal(2, firstStage.Hits.Count); // 挥击 + 首段剑气都在判定帧结算
        Assert.All(firstStage.Hits, h => Assert.True(MatchesBaseDamage(h.Damage, 90f * 1.5f),
            "实际伤害 " + h.Damage));

        // 冷却 0.5s / 1.25 = 6 tick；换装不能提前解锁。
        Assert.False(resolver.Decide(1, Basic(10002, seq: 2, revision: 0, lifeId: player.LifeId), 5).Accepted);
        Assert.True(resolver.Decide(1, Basic(10002, seq: 3, revision: 0, lifeId: player.LifeId), 6).Accepted);
    }

    [Fact]
    public void Cast_Snapshot_Keeps_Original_Weapon_After_Equip_Change()
    {
        var store = new EntityStore();
        var player = AddCaster(store);
        Equip(player, 10, ElementalCatalogDefaults.IceSwordTemplateId, 0);
        store.AddPlayer(2, "target").UpdatePosition(new Vector3(0f, 0f, 1.5f), 0f, 0f, 0L, 0U);

        var catalog = Catalog(iceAttack: 1.5f, iceSpeed: 1f, iceQiMultiplier: 2f,
            fireAttack: 0.5f, fireSpeed: 1f);
        var resolver = new CombatResolver(store, new Random(1), catalog);

        Assert.True(resolver.Decide(1, Basic(10002, seq: 1, revision: 0, lifeId: player.LifeId), 0).Accepted);
        // 在途换装：新的攻击力/剑气倍率不得影响已经接受的施法。
        player.EquippedWeaponInstanceId = 11;
        player.EquippedWeaponTemplateId = ElementalCatalogDefaults.FireSwordTemplateId;
        player.EquipmentRevision = 1;

        var results = DrainTo(resolver, 6);
        var hits = results.SelectMany(r => r.Hits).ToList();
        Assert.Equal(2, hits.Count); // 挥击 + 首段剑气
        Assert.True(MatchesBaseDamage(hits[0].Damage, 90f * 1.5f));            // 挥击不乘剑气倍率
        Assert.True(MatchesBaseDamage(hits[1].Damage, 90f * 1.5f * 2f));       // 剑气乘 2.0
    }

    [Fact]
    public void SwordQi_Damage_Uses_Weapon_Multiplier_And_Shared_Coefficient()
    {
        var store = new EntityStore();
        var player = AddCaster(store);
        Equip(player, 10, ElementalCatalogDefaults.IceSwordTemplateId, 0);
        store.AddPlayer(2, "target").UpdatePosition(new Vector3(0f, 0f, 1.5f), 0f, 0f, 0L, 0U);

        var catalog = Catalog(iceAttack: 1.5f, iceSpeed: 1f, iceQiMultiplier: 1.5f,
            fireAttack: 1f, fireSpeed: 1f);
        catalog.SwordQi.SwordQiDamageCoefficient = 1.2f;
        var resolver = new CombatResolver(store, new Random(1), catalog);

        Assert.True(resolver.Decide(1, Basic(10002, seq: 1, revision: 0, lifeId: player.LifeId), 0).Accepted);
        var results = DrainTo(resolver, 6);
        var hits = results.SelectMany(r => r.Hits).ToList();
        Assert.Equal(2, hits.Count);
        float swingExpected = 90f * 1.5f;
        float qiExpected = 90f * 1.5f * 1.2f * 1.5f;
        Assert.True(MatchesBaseDamage(hits[0].Damage, swingExpected));
        Assert.True(MatchesBaseDamage(hits[1].Damage, qiExpected));
    }

    [Fact]
    public void Duplicate_Cast_Seq_Replays_Original_And_Does_Not_Schedule_Again()
    {
        var store = new EntityStore();
        var player = AddCaster(store);
        Equip(player, 10, ElementalCatalogDefaults.IceSwordTemplateId, 0);
        store.AddPlayer(2, "target").UpdatePosition(new Vector3(0f, 0f, 1.5f), 0f, 0f, 0L, 0U);

        var resolver = new CombatResolver(store, new Random(1));
        var request = Basic(10002, seq: 7, revision: 0, lifeId: player.LifeId);
        var first = resolver.Decide(1, request, 0);
        var replay = resolver.Decide(1, request, 30);

        Assert.True(first.Accepted);
        Assert.True(replay.Accepted);
        Assert.True(replay.Replay);
        Assert.Equal(first.Confirm!.CastId, replay.Confirm!.CastId);
        Assert.Single(DrainTo(resolver, 6));
    }

    [Fact]
    public void Stale_Equipment_Is_Rejected_With_Current_Summary()
    {
        var store = new EntityStore();
        var player = AddCaster(store);
        Equip(player, 10, ElementalCatalogDefaults.IceSwordTemplateId, 2);

        var resolver = new CombatResolver(store);
        var result = resolver.Decide(1, Basic(10001, seq: 1, revision: 1, lifeId: player.LifeId), 0);

        Assert.False(result.Accepted);
        Assert.Equal("stale-equipment", result.RejectReason);
        Assert.Equal(2u, result.Confirm!.EquipmentRevision);
        Assert.Equal(ElementalCatalogDefaults.IceSwordTemplateId, result.Confirm.TemplateId);
        Assert.Equal(1, result.Confirm.ElementId);
    }

    [Fact]
    public void Stale_Life_Is_Rejected()
    {
        var store = new EntityStore();
        var player = AddCaster(store);
        Equip(player, 10, ElementalCatalogDefaults.IceSwordTemplateId, 0);
        player.LifeId = 3;

        var resolver = new CombatResolver(store);
        var result = resolver.Decide(1, Basic(10001, seq: 1, revision: 0, lifeId: 2), 0);

        Assert.False(result.Accepted);
        Assert.Equal("stale-life", result.RejectReason);
    }

    [Fact]
    public void Player_And_Npc_CastIds_Share_One_Monotonic_Allocator()
    {
        var store = new EntityStore();
        var player = AddCaster(store);
        Equip(player, 10, ElementalCatalogDefaults.IceSwordTemplateId, 0);
        var npcA = store.AddNpc(2, "npcA", 1, 100f, new Vector3(0f, 0f, 1f));
        var npcB = store.AddNpc(3, "npcB", 1, 100f, new Vector3(0f, 0f, 2f));

        var resolver = new CombatResolver(store);
        var playerCast = resolver.Decide(1, Basic(10001, seq: 1, revision: 0, lifeId: player.LifeId), 0);
        var npcCastA = resolver.Decide(2, new CastRequest { SkillId = 50001, Shape = new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f } }, 0);
        var npcCastB = resolver.Decide(3, new CastRequest { SkillId = 50001, Shape = new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f } }, 0);

        Assert.True(playerCast.Accepted);
        Assert.True(npcCastA.Accepted);
        Assert.True(npcCastB.Accepted);
        Assert.True(npcCastA.Confirm!.CastId > playerCast.Confirm!.CastId);
        Assert.True(npcCastB.Confirm!.CastId > npcCastA.Confirm.CastId);
    }

    [Fact]
    public void HitEntry_Carries_CastId_And_Target_LifeId()
    {
        var store = new EntityStore();
        var player = AddCaster(store);
        Equip(player, 10, ElementalCatalogDefaults.IceSwordTemplateId, 0);
        var target = store.AddPlayer(2, "target");
        target.UpdatePosition(new Vector3(0f, 0f, 1.5f), 0f, 0f, 0L, 0U);

        var resolver = new CombatResolver(store, new Random(1));
        var decision = resolver.Decide(1, Basic(10002, seq: 1, revision: 0, lifeId: player.LifeId), 0);
        var results = DrainTo(resolver, 6);
        var hit = results.SelectMany(r => r.Hits).First();

        Assert.Equal(decision.Confirm!.CastId, hit.CastId);
        Assert.Equal(target.LifeId, hit.TargetLifeId);
        Assert.Equal(DamageKind.Direct, hit.DamageKind);
    }
}

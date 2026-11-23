using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>Plan 04 任务 3：10002 首个**有效**命中附加元素状态（挥砍/剑气两个实例同组只附一次），
/// 风易伤在同次施法后续命中生效（PvE 2.0 / PvP 1.28）。</summary>
public class ElementalApplicationTests
{
    private const int HeavySkillId = 10002;
    private const uint FirstStageTick = 6;    // 10002 起手 0.35s = ceil(5.25) tick

    private static PlayerState AddCaster(EntityStore store, long id = 1L,
        int weaponTemplateId = ElementalCatalogDefaults.WindSwordTemplateId)
    {
        var player = store.AddPlayer(id, "P" + id);
        player.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 0L, 0U);
        player.EquippedWeaponInstanceId = 1L;
        player.EquippedWeaponTemplateId = weaponTemplateId;
        return player;
    }

    private static PlayerState AddNpcTarget(EntityStore store, float x, float z, float hp = 10_000f)
        => store.AddNpc(2, "N2", templateId: 1, hp: hp, position: new Vector3(x, 0f, z));

    private static PlayerState AddPlayerTarget(EntityStore store, float x, float z)
    {
        var player = store.AddPlayer(2, "P2");
        player.UpdatePosition(new Vector3(x, 0f, z), 0f, 0f, 0L, 0U);
        return player;
    }

    private static CastRequest Heavy(long targetId = 0) => new()
    {
        SkillId = HeavySkillId,
        TargetPlayerId = targetId,
        Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f }
    };

    private static List<HitEntry> Drain(CombatResolver resolver, uint from, uint to)
    {
        var hits = new List<HitEntry>();
        for (uint t = from; t <= to; t++)
            foreach (var resolution in resolver.DrainResolved(t))
                hits.AddRange(resolution.Hits);
        return hits;
    }

    private static float CritScale(HitEntry hit)
        => hit.IsCritical ? CombatRules.CritMultiplier : 1f;

    private static void AssertNoStatuses(PlayerState target)
        => Assert.True(target.Statuses == null || target.Statuses.Runtime.Count == 0,
            "不应附加任何元素状态");

    [Fact]
    public void Swing_First_Hit_Applies_Element_Only_Once()
    {
        // 2m 目标同时吃挥砍（首段 tick 6）与剑气首段：挥砍先结算并附加，剑气同组不得再 Apply。
        var store = new EntityStore();
        AddCaster(store);
        var target = AddNpcTarget(store, x: 0f, z: 2f);
        uint versionBefore = target.CombatVersion;
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var hits = Drain(resolver, 1, 40);

        Assert.Equal(2, hits.Count);                           // 挥砍 + 剑气各一个伤害实例
        var status = Assert.Single(target.Statuses!.Runtime.Active);
        Assert.Equal(401, status.EffectId);                    // 风
        Assert.Equal(HeavySkillId, status.SourceSkillId);
        Assert.Equal(FirstStageTick, status.AppliedTick);      // 由挥砍（首段）附加
        // 只 Apply 一次：两次扣血 bump + 一次状态 bump = +3；剑气若再次 Apply（刷新）会再多 +1。
        Assert.Equal(versionBefore + 3u, target.CombatVersion);
    }

    [Fact]
    public void SwordQi_First_Hit_Applies_Element()
    {
        // 6m 在挥砍锥（2.5m）外、剑气第 1 段 [4,8] 内：由剑气附加，判定 tick = 6 + 4。
        var store = new EntityStore();
        AddCaster(store);
        var target = AddNpcTarget(store, x: 0f, z: 6f);
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var hit = Assert.Single(Drain(resolver, 1, 40));

        Assert.Equal(90f, hit.Damage, 0.001f);                 // 附加自身不吃新倍率
        var status = Assert.Single(target.Statuses!.Runtime.Active);
        Assert.Equal(401, status.EffectId);
        Assert.Equal(10u, status.AppliedTick);                 // 第 1 段判定 tick
    }

    [Fact]
    public void Blocked_Hit_Does_Not_Apply_Element()
    {
        var store = new EntityStore();
        AddCaster(store);
        var target = AddPlayerTarget(store, x: 0f, z: 2f);
        target.Rotation = 180f;                                // 面向施法者
        target.IsDefending = true;
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var hits = Drain(resolver, 1, 40);

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.True(h.IsBlocked));
        AssertNoStatuses(target);
    }

    [Fact]
    public void Zero_Damage_Hit_Does_Not_Apply_Element()
    {
        // 武器 BaseAttack=0 ⇒ 挥砍/剑气伤害均为 0；元素附加条件要求 damage > 0。
        var store = new EntityStore();
        AddCaster(store);
        var target = AddNpcTarget(store, x: 0f, z: 2f);
        var catalog = ElementalCatalogDefaults.Create();
        catalog.Weapons.Find(w => w.TemplateId == ElementalCatalogDefaults.WindSwordTemplateId)!
            .BaseAttack = 0f;
        var resolver = new CombatResolver(store, new Random(1), catalog);

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var hits = Drain(resolver, 1, 40);

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal(0f, h.Damage, 0.001f));
        AssertNoStatuses(target);
    }

    [Fact]
    public void Lethal_Swing_Does_Not_Apply_Element_To_Dead_Target()
    {
        // 50 HP 被挥砍（90）击杀：剑气首段虽已选中同一目标，也不再产生伤害事件。
        var store = new EntityStore();
        AddCaster(store);
        var target = AddNpcTarget(store, x: 0f, z: 2f, hp: 50f);
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var kill = Assert.Single(Drain(resolver, 1, 40));

        Assert.False(target.IsAlive);
        Assert.Equal(50f - kill.Damage, target.Hp);
        Assert.Equal(1u, target.NextEventSeq);
        AssertNoStatuses(target);
    }

    [Fact]
    public void Wind_Amplifies_Same_Cast_Later_Hit_Pve_By_Two()
    {
        var store = new EntityStore();
        AddCaster(store);
        AddNpcTarget(store, x: 0f, z: 2f);
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var hits = Drain(resolver, 1, 40);

        Assert.Equal(2, hits.Count);
        Assert.Equal(90f * CritScale(hits[0]), hits[0].Damage, 0.001f);   // 挥砍：附加前，无易伤
        Assert.Equal(90f * CritScale(hits[1]) * 2f, hits[1].Damage, 0.001f); // 同次剑气：PvE 风倍率 2.0
    }

    [Fact]
    public void Wind_Amplifies_Same_Cast_Later_Hit_Pvp_By_Config_Multiplier()
    {
        var store = new EntityStore();
        AddCaster(store);
        var target = AddPlayerTarget(store, x: 0f, z: 2f);
        target.SetMaxHp(720f);
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var hits = Drain(resolver, 1, 40);

        Assert.Equal(2, hits.Count);
        Assert.Equal(90f * CritScale(hits[0]), hits[0].Damage, 0.001f);
        Assert.Equal(90f * CritScale(hits[1]) * 1.28f, hits[1].Damage, 0.001f); // PvP 风倍率 1.28
    }

    [Fact]
    public void Non_Heavy_Skill_Does_Not_Apply_Element()
    {
        var store = new EntityStore();
        AddCaster(store);
        var target = AddNpcTarget(store, x: 0f, z: 1.5f);
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, new CastRequest
        {
            SkillId = 10001,
            Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
        }, tick: 0).Accepted);
        var hits = Drain(resolver, 1, 20);

        Assert.Single(hits);
        AssertNoStatuses(target);
    }

    [Fact]
    public void Default_Weapon_Does_Not_Apply_Element()
    {
        // ElementId=0（默认剑）：10002 也不附加任何状态。
        var store = new EntityStore();
        AddCaster(store, weaponTemplateId: ElementalCatalogDefaults.DefaultSwordTemplateId);
        var target = AddNpcTarget(store, x: 0f, z: 2f);
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var hits = Drain(resolver, 1, 40);

        Assert.Equal(2, hits.Count);
        AssertNoStatuses(target);
    }
}

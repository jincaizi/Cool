using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class CombatResolverProjectileTests
{
    /// <summary>施法者 = 玩家 1，位于原点、yaw 0（→ +Z）、满血存活（Position/Rotation 默认值即可）。
    /// 目标 = Npc（`AddNpc` 是 public 的位置入口；`PlayerState.Position` 是 internal set）。
    /// id 固定：target=2 / second=3 / third=4。</summary>
    private static EntityStore MakeStore((float x, float z) targetAt, (float x, float z)? secondAt = null,
        (float x, float z)? thirdAt = null)
    {
        var store = new EntityStore();
        store.AddPlayer(1, "caster");
        store.AddNpc(2, "target", templateId: 1, hp: 100f, position: new Vector3(targetAt.x, 0f, targetAt.z));
        if (secondAt.HasValue)
            store.AddNpc(3, "second", 1, 100f, new Vector3(secondAt.Value.x, 0f, secondAt.Value.z));
        if (thirdAt.HasValue)
            store.AddNpc(4, "third", 1, 100f, new Vector3(thirdAt.Value.x, 0f, thirdAt.Value.z));
        return store;
    }

    private static List<CombatResolver.CastResolution> Drain(CombatResolver resolver, uint untilTick)
    {
        var all = new List<CombatResolver.CastResolution>();
        for (uint t = 1; t <= untilTick; t++) all.AddRange(resolver.DrainResolved(t));
        return all;
    }

    [Fact]
    public void SwordQi_Line_Hits_Target_Outside_Swing_Cone()
    {
        // 目标在 6m 正前方：挥击锥（2.5m）打不到，剑气的 12m 线可以。
        // 挥击实例仍在首段结算（10002 的 KeepsSwing=true），只是该目标在锥外 ⇒ 只有线形一条。
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 6f)), new Random(1));
        var request = new CastRequest
        {
            SkillId = 10002,
            Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f }
        };
        Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

        var hits = Drain(resolver, 40).SelectMany(r => r.Hits).ToList();
        var hit = Assert.Single(hits);                       // 只有线形实例
        Assert.Equal(2L, hit.TargetPlayerId);
    }

    [Fact]
    public void SwordQi_Close_Target_Inside_Swing_Cone_Takes_Swing_And_Line_Instances()
    {
        // 规格 §3.2：剑气与挥击是两个独立伤害实例（与单机一致，不跨组去重）。
        // 目标在 2m：既在挥击锥（2.5m）内，也在剑气首段 [0,4] 的线上 ⇒ 两次结算、两个 HitEntry。
        // 删掉 ResolveStage 里的挥击实例（或把 KeepsSwing 改回 false）本条必红。
        var store = MakeStore(targetAt: (0f, 2f));
        var resolver = new CombatResolver(store, new Random(1));
        var request = new CastRequest
        {
            SkillId = 10002,
            Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f }
        };
        Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

        var hits = Drain(resolver, 40).SelectMany(r => r.Hits)
            .Where(h => h.TargetPlayerId == 2L).ToList();

        Assert.Equal(2, hits.Count);                          // 挥击 + 剑气
        Assert.All(hits, h => Assert.Equal(90f * (h.IsCritical ? 1.5f : 1f), h.Damage, 0.001f));
        Assert.Equal(100f - hits[0].Damage - hits[1].Damage, store.GetPlayer(2)!.Hp, 0.001f);
    }

    [Fact]
    public void SwordQi_Shared_Stage_Boundary_Hits_Target_Exactly_Once()
    {
        // 目标放在 4m = 3 段剑气（每段 4m）的分段边界上：第 0 段 [0,4] 与第 1 段 [4,8] 都会选中它。
        // GroupHitIds 必须把组内重复压掉——删掉 ResolveStage 里的去重守卫应让本条变红。
        var store = MakeStore(targetAt: (0f, 4f));
        var resolver = new CombatResolver(store, new Random(1));
        Assert.True(resolver.Decide(1, new CastRequest { SkillId = 10002 }, tick: 0).Accepted);

        var hits = Drain(resolver, 40).SelectMany(r => r.Hits)
            .Where(h => h.TargetPlayerId == 2L).ToList();

        var hit = Assert.Single(hits);                       // 恰好结算一次（不是每段一次）
        Assert.Equal(100f - hit.Damage, store.GetPlayer(2)!.Hp, 0.001f); // HP 只掉一次
    }

    [Fact]
    public void Fireball_Circle_At_Impact_Hits_Targets_In_Radius()
    {
        // 落点 (0,0,4)；目标 2 在落点中心、4 距 0.5m（半径 1.5 内）、3 在 9m 外。
        var resolver = new CombatResolver(
            MakeStore(targetAt: (0f, 4f), secondAt: (0f, 9f), thirdAt: (0f, 4.5f)), new Random(1));
        var request = new CastRequest { SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 4f };
        Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

        var hitIds = Drain(resolver, 40).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.Contains(2L, hitIds);
        Assert.Contains(4L, hitIds);
        Assert.DoesNotContain(3L, hitIds);
    }

    [Fact]
    public void Fireball_Does_Not_Hit_Along_Path_Outside_Impact_Circle()
    {
        // 目标在 4m，落点在 10m：4m 处不在 1.5m 圆内，线形旧行为不得残留。
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 4f)), new Random(1));
        var request = new CastRequest { SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 10f };
        Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

        var hitIds = Drain(resolver, 40).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.DoesNotContain(2L, hitIds);
    }

    [Fact]
    public void Fireball_Impact_Too_Far_Is_Rejected()
    {
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
        var far = new CastRequest { SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 15.5f };
        var decision = resolver.Decide(1, far, tick: 0);
        Assert.False(decision.Accepted);
        Assert.Equal("impact-too-far", decision.RejectReason);
    }

    [Fact]
    public void Fireball_Travel_Delay_Defers_First_Hit()
    {
        // 10m / 18 m/s × 15Hz = 8.33 → ceil = 9；tick 8 不得结算，tick 9 结算。
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
        var request = new CastRequest { SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 10f };
        Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

        Assert.Empty(resolver.DrainResolved(8).SelectMany(r => r.Hits));
        Assert.NotEmpty(resolver.DrainResolved(9).SelectMany(r => r.Hits));
    }

    [Fact]
    public void Fireball_Charge_Progress_Does_Not_Scale_Damage()
    {
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
        var request = new CastRequest
        {
            SkillId = 20003, ImpactX = 0f, ImpactY = 0f, ImpactZ = 10f, ChargeProgress = 1f
        };
        Assert.True(resolver.Decide(1, request, tick: 0).Accepted);

        var hit = Drain(resolver, 40).SelectMany(r => r.Hits).First(h => h.TargetPlayerId == 2L);
        Assert.Equal(20f, hit.Damage, 0.001f);
    }

    [Fact]
    public void Projectile_Travel_Ticks_Clamps_NonFinite_And_Zero_Distance()
    {
        Assert.True(CombatRules.TryGet(20003, out var entry));
        Assert.Equal(9u, CombatRules.ProjectileTravelTicks(entry.Projectile, 0f, 0f, 0f, 10f));
        Assert.Equal(0u, CombatRules.ProjectileTravelTicks(entry.Projectile, 0f, 0f, float.NaN, 0f));
        Assert.Equal(0u, CombatRules.ProjectileTravelTicks(entry.Projectile, 0f, 0f, 0f, 0f));
        Assert.Equal(0u, CombatRules.ProjectileTravelTicks(null, 0f, 0f, 0f, 10f));
    }

    [Fact]
    public void ChargeBlast_Scales_Radius_And_Damage_By_Charge()
    {
        // 目标距施法者 10m；落点 = 目标位置 ⇒ 40 × 1.5（满蓄力、无暴击）= 60
        // Shape 必须带客户端真实形状（20004 资产 _targetType=1 / _range=2 / _areaRadius=2 /
        // _angle=120）——ValidateShape 会拒绝全零默认形状（brief 原用例漏写，见任务报告偏差 1）
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
        var charge = new CastRequest
        {
            SkillId = 20004, ImpactX = 0f, ImpactY = 0f, ImpactZ = 10f, ChargeProgress = 1f,
            Shape = new HitShape { ShapeType = 1, Range = 2f, AreaRadius = 2f, Angle = 120f }
        };
        Assert.True(resolver.Decide(1, charge, tick: 0).Accepted);

        var hit = Drain(resolver, 20).SelectMany(r => r.Hits).FirstOrDefault(h => h.TargetPlayerId == 2L);
        Assert.NotNull(hit);
        Assert.Equal(60f, hit!.Damage, 0.001f);
    }

    [Fact]
    public void ChargeBlast_Radius_Scales_With_Charge()
    {
        // 目标 (0,5)、落点 (0,2.5)：目标距爆点 2.5m。
        // ChargeProgress=0 ⇒ 半径 2（2.5 > 2，打不到）；=1 ⇒ 半径 2 × 1.5 = 3（打得到）。
        var shape = new HitShape { ShapeType = 1, Range = 2f, AreaRadius = 2f, Angle = 120f };

        var missResolver = new CombatResolver(MakeStore(targetAt: (0f, 5f)), new Random(1));
        var miss = new CastRequest
        {
            SkillId = 20004, ImpactX = 0f, ImpactY = 0f, ImpactZ = 2.5f, ChargeProgress = 0f, Shape = shape
        };
        Assert.True(missResolver.Decide(1, miss, tick: 0).Accepted);
        var missHitIds = Drain(missResolver, 20).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.DoesNotContain(2L, missHitIds);   // 半径 2 够不到 2.5m

        var hitResolver = new CombatResolver(MakeStore(targetAt: (0f, 5f)), new Random(1));
        var hit = new CastRequest
        {
            SkillId = 20004, ImpactX = 0f, ImpactY = 0f, ImpactZ = 2.5f, ChargeProgress = 1f, Shape = shape
        };
        Assert.True(hitResolver.Decide(1, hit, tick: 0).Accepted);
        var hitHitIds = Drain(hitResolver, 20).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.Contains(2L, hitHitIds);          // 半径 3 够得到 2.5m
    }

    [Fact]
    public void Meteor_Hits_Only_Inside_Impact_Radius()
    {
        // 落点 (0,0,4)；目标 (0,0,1) 距落点 3m < 4 ⇒ 落点圆命中。
        // 但请求未带 Shape（Range=0）⇒ 旧的挥击判定打不到 1m 外的目标——退回旧实现本条必红。
        // 另一个 (0,0,10) 距落点 6m ⇒ 圆外。
        var store = MakeStore(targetAt: (0f, 1f), secondAt: (0f, 10f));
        var resolver = new CombatResolver(store, new Random(1));
        var meteor = new CastRequest { SkillId = 30001, ImpactX = 0f, ImpactY = 0f, ImpactZ = 4f };
        Assert.True(resolver.Decide(1, meteor, tick: 0).Accepted);

        var hitIds = Drain(resolver, 30).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.Contains(2L, hitIds);
        Assert.DoesNotContain(3L, hitIds);
    }

    [Fact]
    public void Impact_Too_Far_Is_Rejected()
    {
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
        var far = new CastRequest { SkillId = 30001, ImpactX = 0f, ImpactY = 0f, ImpactZ = 31f };
        var decision = resolver.Decide(1, far, tick: 0);
        Assert.False(decision.Accepted);
        Assert.Equal("impact-too-far", decision.RejectReason);
    }

    [Fact]
    public void Impact_Not_Finite_Is_Rejected()
    {
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 10f)), new Random(1));
        var nan = new CastRequest { SkillId = 30001, ImpactX = float.NaN, ImpactY = 0f, ImpactZ = 4f };
        var decision = resolver.Decide(1, nan, tick: 0);
        Assert.False(decision.Accepted);
        Assert.Equal("impact-not-finite", decision.RejectReason);
    }
}

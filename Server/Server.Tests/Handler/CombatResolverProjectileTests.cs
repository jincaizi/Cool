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
    /// 目标 = Npc（`AddNpc` 是 public 的位置入口；`PlayerState.Position` 是 internal set）。</summary>
    private static EntityStore MakeStore((float x, float z) targetAt, (float x, float z)? secondAt = null)
    {
        var store = new EntityStore();
        store.AddPlayer(1, "caster");
        store.AddNpc(2, "target", templateId: 1, hp: 100f, position: new Vector3(targetAt.x, 0f, targetAt.z));
        if (secondAt.HasValue)
            store.AddNpc(3, "second", 1, 100f, new Vector3(secondAt.Value.x, 0f, secondAt.Value.z));
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
        // 目标在 6m 正前方：挥击锥（2.5m）打不到，剑气的 12m 线可以
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 6f)), new Random(1));
        Assert.True(resolver.Decide(1, new CastRequest { SkillId = 10002 }, tick: 0).Accepted);

        var hitIds = Drain(resolver, 40).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.Contains(2L, hitIds);
    }

    [Fact]
    public void Fireball_Explodes_At_First_Target_And_Stops()
    {
        var resolver = new CombatResolver(MakeStore(targetAt: (0f, 4f), secondAt: (0f, 9f)), new Random(1));
        Assert.True(resolver.Decide(1, new CastRequest { SkillId = 20003 }, tick: 0).Accepted);

        var hitIds = Drain(resolver, 40).SelectMany(r => r.Hits).Select(h => h.TargetPlayerId).ToList();
        Assert.Contains(2L, hitIds);
        Assert.DoesNotContain(3L, hitIds);   // 首中即停：后方 9m 的目标不该被同一次施法命中
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
    public void Meteor_Hits_Only_Inside_Impact_Radius()
    {
        // 落点 (0,0,4)；目标在 (0,0,0) 距落点 4m ⇒ 在半径边界内；另一个 (0,0,10) 距落点 6m ⇒ 外
        var store = MakeStore(targetAt: (0f, 0f), secondAt: (0f, 10f));
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
        Assert.False(resolver.Decide(1, far, tick: 0).Accepted);
    }
}

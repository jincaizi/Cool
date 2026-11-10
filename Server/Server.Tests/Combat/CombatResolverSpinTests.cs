using System;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// B3：R（20002）联网多段判定。单机时间轴：起手剪辑 0.2s + 0.2s 首判，
/// 之后每 0.2s 一段，持续 3s，每目标最多 5 段。服务端必须逐段按当时的施法者位置
/// 与存活目标重新判定，而不是激活 tick 只判一次。
/// </summary>
public class CombatResolverSpinTests
{
    private const uint DecideTick = 10;

    /// <summary>0.4s 首判 = 6 tick @15Hz；之后每 0.2s = 3 tick。</summary>
    private const uint FirstStageTick = DecideTick + 6;
    private const uint StageTickStep = 3;

    private static (EntityStore Store, CombatResolver Resolver, long Caster, long Target) World(float targetZ)
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P");
        caster.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);
        var target = store.AddPlayer(2, "T");
        target.UpdatePosition(new Vector3(0f, 0f, targetZ), 0f, 0f, 1L, 0U);
        // 抬高血量：首段 160 伤害会直接击杀 100 HP 的靶子，Alive 过滤会让后续段假阴性。
        target.ApplyDamage(-10000f);
        return (store, new CombatResolver(store, new Random(42)), 1, 2);
    }

    private static CastRequest Spin() => new CastRequest
    {
        SkillId = 20002,
        Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f, Angle = 120f }
    };

    [Fact]
    public void Spin_Hits_A_Target_That_Enters_After_The_First_Judgment()
    {
        var (store, resolver, caster, target) = World(targetZ: 5f); // 起手在圈外
        Assert.True(resolver.Decide(caster, Spin(), DecideTick).Accepted);
        resolver.DrainResolved(DecideTick); // 主循环每 tick 都 drain：激活 tick 不能让单段实现"提前"结算

        store.GetPlayer(target)!.UpdatePosition(new Vector3(0f, 0f, 1.5f), 0f, 0f, 1L, 0U);

        var firstStage = Assert.Single(resolver.DrainResolved(FirstStageTick));
        Assert.Single(firstStage.Hits);
    }

    [Fact]
    public void Spin_Hits_Each_Target_At_Most_Five_Times()
    {
        var (store, resolver, caster, target) = World(targetZ: 1.5f);
        Assert.True(resolver.Decide(caster, Spin(), DecideTick).Accepted);

        int hits = 0;
        for (uint t = FirstStageTick; t <= FirstStageTick + 20 * StageTickStep; t += StageTickStep)
            foreach (var resolution in resolver.DrainResolved(t))
                hits += resolution.Hits.Count;

        Assert.Equal(5, hits);
        Assert.True(store.GetPlayer(target)!.IsAlive);
    }

    [Fact]
    public void Spin_Stops_Immediately_When_Cancelled()
    {
        var (store, resolver, caster, target) = World(targetZ: 1.5f);
        Assert.True(resolver.Decide(caster, Spin(), DecideTick).Accepted);
        resolver.DrainResolved(DecideTick);
        Assert.Single(Assert.Single(resolver.DrainResolved(FirstStageTick)).Hits);

        Assert.Equal(1, resolver.CancelPending(caster, 20002));
        float hpAfterCancel = store.GetPlayer(target)!.Hp;

        for (uint t = FirstStageTick + StageTickStep;
             t <= FirstStageTick + 10 * StageTickStep; t += StageTickStep)
            Assert.Empty(resolver.DrainResolved(t));

        Assert.Equal(hpAfterCancel, store.GetPlayer(target)!.Hp, 4);
    }

    [Fact]
    public void Spin_Pending_Is_Discarded_When_Caster_Dies()
    {
        var (store, resolver, caster, target) = World(targetZ: 1.5f);
        Assert.True(resolver.Decide(caster, Spin(), DecideTick).Accepted);
        resolver.DrainResolved(DecideTick);                     // 主循环每 tick 都 drain
        Assert.Single(Assert.Single(resolver.DrainResolved(FirstStageTick)).Hits);

        store.GetPlayer(caster)!.ApplyDamage(1000f);            // 施法者死亡
        resolver.DrainResolved(FirstStageTick + StageTickStep); // 死亡后的下一段：整组丢弃

        store.GetPlayer(caster)!.ApplyDamage(-10000f);          // 复活（权威 HP 回到正值）
        float hpAfterDeath = store.GetPlayer(target)!.Hp;

        for (uint t = FirstStageTick + 2 * StageTickStep;
             t <= FirstStageTick + 10 * StageTickStep; t += StageTickStep)
            Assert.Empty(resolver.DrainResolved(t));

        Assert.Equal(hpAfterDeath, store.GetPlayer(target)!.Hp, 4);
    }
}

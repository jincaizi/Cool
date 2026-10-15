using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>N3：决策 / 判定两段化 + 判定帧延迟（resolveTick = decideTick + HitDelayTicks）——
/// 前摇窗口内目标移出射程 / 绕到身后 / 施法者死亡都会改变结局。</summary>
public class CastResolutionSchedulingTests
{
    private static (EntityStore Store, CombatResolver Resolver, long Caster, long Target) World(float targetZ)
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P");
        caster.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);
        var target = store.AddPlayer(2, "T");
        target.UpdatePosition(new Vector3(0f, 0f, targetZ), 0f, 0f, 1L, 0U);
        return (store, new CombatResolver(store, new System.Random(42)), 1, 2);
    }

    private static CastRequest Swing(long targetId = 0) => new CastRequest
    {
        SkillId = 10001, // BA_Combo1：Single range 2
        TargetPlayerId = targetId,
        Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
    };

    [Fact]
    public void Decide_Returns_Broadcast_But_No_Hits()
    {
        var (store, resolver, caster, target) = World(1.5f);

        var decision = resolver.Decide(caster, Swing(), 10);

        Assert.True(decision.Accepted);
        Assert.NotNull(decision.Broadcast);
        Assert.Equal(caster, decision.Broadcast!.CasterPlayerId);
        Assert.Equal(10001, decision.Broadcast.SkillId);
        Assert.NotEqual(0L, decision.Broadcast.ServerTimestamp);  // 决策时刻（wall-clock）
        Assert.Empty(decision.Hits);                              // 判定还没发生
        Assert.Equal(100f, store.GetPlayer(target)!.Hp, 4);       // 目标未掉血
    }

    [Fact]
    public void DrainResolved_At_The_Deadline_Yields_This_Casts_Hits()
    {
        var (store, resolver, caster, target) = World(1.5f);
        resolver.Decide(caster, Swing(), 10);

        Assert.Empty(resolver.DrainResolved(10));   // N3：判定帧未到，决策 tick 不结算

        var resolved = resolver.DrainResolved(10 + CombatRules.HitDelayTicks(EntryOf(10001)));

        var one = Assert.Single(resolved);
        Assert.Equal(caster, one.Broadcast!.CasterPlayerId);
        var hit = Assert.Single(one.Hits);
        Assert.Equal(target, hit.TargetPlayerId);
        Assert.Equal(100f - hit.Damage, store.GetPlayer(target)!.Hp, 4);
    }

    [Fact]
    public void Target_Leaving_Range_During_The_Window_Misses()
    {
        var (store, resolver, caster, target) = World(1.5f);
        resolver.Decide(caster, Swing(), 10);                       // 决策：目标在射程内
        resolver.DrainResolved(10);                                 // 模拟 tick 循环：决策那一 tick 也会 drain
        store.GetPlayer(target)!.UpdatePosition(new Vector3(0f, 0f, 5f), 0f, 0f, 1L, 0U);

        var one = Assert.Single(resolver.DrainResolved(10 + CombatRules.HitDelayTicks(EntryOf(10001))));

        Assert.Empty(one.Hits);                                     // 判定帧已出射程 → 空挥
        Assert.Equal(100f, store.GetPlayer(target)!.Hp, 4);
    }

    [Fact]
    public void Target_Circling_Behind_During_The_Window_Misses()
    {
        // 绕背躲怪：判定帧在身后（夹角 180° > 半锥 60°）
        var (store, resolver, caster, target) = World(1.5f);
        resolver.Decide(caster, Swing(), 10);
        resolver.DrainResolved(10);                                 // 模拟 tick 循环
        store.GetPlayer(target)!.UpdatePosition(new Vector3(0f, 0f, -1.5f), 0f, 0f, 1L, 0U);

        var one = Assert.Single(resolver.DrainResolved(10 + CombatRules.HitDelayTicks(EntryOf(10001))));

        Assert.Empty(one.Hits);
    }

    [Fact]
    public void Caster_Dying_Before_The_Judgment_Drops_The_Swing()
    {
        var (store, resolver, caster, target) = World(1.5f);
        resolver.Decide(caster, Swing(), 10);
        resolver.DrainResolved(10);                                 // 模拟 tick 循环（此刻施法者还活着）
        store.GetPlayer(caster)!.ApplyDamage(1000f);                // 打断抬手 = 免伤

        var resolved = resolver.DrainResolved(10 + CombatRules.HitDelayTicks(EntryOf(10001)));

        Assert.Empty(resolved);                                     // 整条结果被丢弃（不下发 DamageResult）
        Assert.Equal(100f, store.GetPlayer(target)!.Hp, 4);
    }

    [Fact]
    public void Zero_Delay_Skill_Resolves_In_The_Same_Tick()
    {
        var (store, resolver, caster, target) = World(1.5f);
        var spin = new CastRequest
        {
            SkillId = 20002,                                        // HitDelay = 0（D-9 向后兼容）
            Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f, Angle = 120f }
        };
        resolver.Decide(caster, spin, 10);

        var one = Assert.Single(resolver.DrainResolved(10));

        Assert.Single(one.Hits);
    }

    [Fact]
    public void Packet_Path_Decides_On_The_Tick_That_Is_About_To_Run()
    {
        // F17：包处理发生在 tick 处理之前，`CurrentTick` 此刻仍是上一 tick 的值——
        // CastHandler.HandleCastAsync 因此传 CurrentTick + 1。本用例钉住这个换算口径本身：
        // 决策 tick = T 时，判定发生在 T + HitDelayTicks（早一 tick 或晚一 tick 都是缺陷）。
        var (store, resolver, caster, target) = World(1.5f);
        const uint decideTick = 37;
        resolver.Decide(caster, Swing(), decideTick);

        Assert.Empty(resolver.DrainResolved(decideTick + CombatRules.HitDelayTicks(EntryOf(10001)) - 1));
        var one = Assert.Single(resolver.DrainResolved(decideTick + CombatRules.HitDelayTicks(EntryOf(10001))));
        Assert.Single(one.Hits);
    }

    private static SkillEntry EntryOf(int skillId)
    {
        CombatRules.TryGet(skillId, out var entry);
        return entry!;
    }
}

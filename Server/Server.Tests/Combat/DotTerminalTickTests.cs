using System.Collections.Generic;
using System.Linq;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>Plan 04 任务 4：末跳与错相/不整除的 EndTick 移除语义
/// （状态区间 [ApplyTick, EndTick)，灼烧允许在 EndTick 结算排定末跳后移除；
/// 风在 EndTick 先失效，不放大同 tick 的灼烧末跳）。</summary>
public class DotTerminalTickTests
{
    [Fact]
    public void Terminal_Tick_Resolves_Scheduled_Last_Tick_Exactly_Once()
    {
        using var h = new DotTestHarness();
        var catalog = h.Catalog;
        var target = h.Store.AddPlayer(2, "Target");
        target.SetMaxHp(100f);
        DotDamageTests.Attach(target, catalog, DotDamageTests.BurnOf(catalog), sourceId: 1, tick: 0);
        h.Bind(target.PlayerId, sessionId: 102);

        var service = h.NewService();
        foreach (uint tick in new uint[] { 14, 15, 30, 44, 45, 46 }) service.Tick(tick);

        // 15 / 30 / 45 三次；45 的排定末跳只结算一次，46 不再产生。
        var hits = h.DotHits();
        Assert.Equal(3, hits.Count);
        Assert.Equal(76f, target.Hp, 3);
        Assert.Empty(target.Statuses!.Runtime.Active);
    }

    [Fact]
    public void Wind_Expiring_On_Burn_EndTick_Does_Not_Amplify_Terminal_Tick()
    {
        using var h = new DotTestHarness();
        var catalog = h.Catalog;
        var target = h.Store.AddNpc(2, "Npc", templateId: 1, hp: 10_000f,
            position: new Vector3(0f, 0f, 0f));
        DotDamageTests.Attach(target, catalog, DotDamageTests.BurnOf(catalog), sourceId: 1, tick: 0);
        h.Bind(target.PlayerId, sessionId: 102);

        var service = h.NewService();
        service.Tick(15);                                  // 第一跳：8（无风）
        service.Tick(22);                                  // 对齐风与灼烧的 EndTick = 45
        DotDamageTests.Attach(target, catalog, DotDamageTests.WindOf(catalog), sourceId: 1, tick: 22);
        service.Tick(30);                                  // 第二跳：风仍生效 → 8 × 2
        service.Tick(44);
        service.Tick(45);                                  // 末跳：风先失效 → 8，不放大

        var damages = h.DotHits().Select(hit => hit.Damage).ToArray();
        Assert.Equal(new[] { 8f, 16f, 8f }, damages.Select(d => (float)System.Math.Round(d, 3)).ToArray());
        Assert.Empty(target.Statuses!.Runtime.Active);
    }

    [Fact]
    public void Off_Phase_Refresh_Removes_At_EndTick_Without_Extra_Tick()
    {
        using var h = new DotTestHarness();
        var catalog = h.Catalog;
        var burn = DotDamageTests.BurnOf(catalog);
        var target = h.Store.AddNpc(2, "Npc", templateId: 1, hp: 10_000f,
            position: new Vector3(0f, 0f, 0f));
        DotDamageTests.Attach(target, catalog, burn, sourceId: 1, tick: 0);
        h.Bind(target.PlayerId, sessionId: 102);

        var service = h.NewService();
        service.Tick(15);
        service.Tick(30);
        DotDamageTests.Attach(target, catalog, burn, sourceId: 1, tick: 32); // EndTick 77，NextTick 仍 45
        foreach (uint tick in new uint[] { 45, 60, 75, 76, 77, 78 }) service.Tick(tick);

        // 45/60/75 三跳，77（EndTick）没有排定跳 → 直接移除，不补跳。
        Assert.Equal(5, h.DotHits().Count);
        Assert.Empty(target.Statuses!.Runtime.Active);
    }

    [Fact]
    public void Non_Dividing_Interval_Removes_At_EndTick_Without_Extra_Tick()
    {
        using var h = new DotTestHarness();
        var catalog = h.Catalog;
        var burn = DotDamageTests.BurnOf(catalog);
        var oddBurn = new StatusEffectDefinition
        {
            EffectId = burn.EffectId,
            EffectKind = burn.EffectKind,
            StackGroupId = burn.StackGroupId,
            DurationSeconds = 3f,      // 45 tick
            IntervalSeconds = 1.1f,    // 17 tick —— 17/34 之后 51 > EndTick
            DamagePerTick = burn.DamagePerTick
        };
        var target = h.Store.AddNpc(2, "Npc", templateId: 1, hp: 10_000f,
            position: new Vector3(0f, 0f, 0f));
        DotDamageTests.Attach(target, catalog, oddBurn, sourceId: 1, tick: 0);
        h.Bind(target.PlayerId, sessionId: 102);

        var service = h.NewService();
        foreach (uint tick in new uint[] { 17, 34, 44, 45, 46 }) service.Tick(tick);

        Assert.Equal(2, h.DotHits().Count);
        Assert.Empty(target.Statuses!.Runtime.Active);
    }
}

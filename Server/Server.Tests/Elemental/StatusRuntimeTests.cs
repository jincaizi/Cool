using System.Collections.Generic;
using System.Linq;
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class StatusRuntimeTests
{
    [Fact]
    public void Apply_SameGroup_Refreshes_EndTick_And_Keeps_Burn_Source()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        var runtime = new StatusRuntime();
        runtime.Configure(catalog.Pvp);

        Assert.Equal(StatusApplyResult.Applied,
            runtime.Apply(burn, sourceId: 1, skillId: 10002, castId: 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true));

        // tick 15 先结算原排定跳（状态 tick 先于输入），再被新命中刷新。
        var dueAt15 = new List<ActiveStatus>();
        runtime.CollectDueDots(15, dueAt15);
        runtime.AdvanceDot(dueAt15[0], 15);

        Assert.Equal(StatusApplyResult.Refreshed,
            runtime.Apply(burn, sourceId: 2, skillId: 10002, castId: 8, tick: 15, targetIsPlayer: false, sourceIsPlayer: true));

        var active = runtime.Active.Single();
        Assert.Equal(1L, active.SourceId);          // 首次施加者保留
        Assert.Equal(60u, active.EndTick);          // 15 + 45
        Assert.Equal(30u, active.NextTick);         // 原下一跳 30 与新间隔 30 取较早
    }

    [Fact]
    public void Burn_Ticks_On_Interval_And_Removes_After_EndTick_Tick()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        var runtime = new StatusRuntime();
        runtime.Configure(catalog.Pvp);
        runtime.Apply(burn, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);

        var due = new List<ActiveStatus>();
        runtime.CollectDueDots(15, due);
        Assert.Single(due);
        runtime.AdvanceDot(due[0], 15);
        Assert.Single(runtime.Active);

        due.Clear();
        runtime.CollectDueDots(45, due);
        Assert.Single(due);                          // EndTick 仍结算
        runtime.AdvanceDot(due[0], 45);
        Assert.Empty(runtime.Active);
    }

    [Fact]
    public void Wind_Expires_Before_Burn_On_Same_EndTick()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var wind = catalog.Statuses.Find(s => s.EffectId == 401)!;
        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        var runtime = new StatusRuntime();
        runtime.Configure(catalog.Pvp);
        runtime.Apply(wind, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);
        runtime.Apply(burn, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);

        runtime.RemoveExpiredModifiers(23);           // wind 1.5s = 23 ticks
        Assert.DoesNotContain(runtime.Active, s => s.EffectId == 401);
        Assert.Equal(1f, runtime.IncomingDamageMultiplier(pvp: false, pvpMultiplier: 1.28f), 4);
    }

    [Fact]
    public void Control_Derives_SpeedScale_And_ActionLock()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var ice = catalog.Statuses.Find(s => s.EffectId == 101)!;
        var thunder = catalog.Statuses.Find(s => s.EffectId == 301)!;
        var runtime = new StatusRuntime();
        runtime.Configure(catalog.Pvp);
        runtime.Apply(ice, 1, 10002, 7, 0, false, true);
        runtime.Apply(thunder, 1, 10002, 7, 0, false, true);

        runtime.DeriveControl(out float speed, out bool locked);
        Assert.Equal(0.7f, speed, 4);
        Assert.True(locked);
    }

    [Fact]
    public void Pvp_Control_Immunity_And_Vulnerability_NoRefresh()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var thunder = catalog.Statuses.Find(s => s.EffectId == 301)!;
        var wind = catalog.Statuses.Find(s => s.EffectId == 401)!;
        var runtime = new StatusRuntime();
        runtime.Configure(catalog.Pvp);

        runtime.Apply(thunder, 1, 10002, 7, 0, targetIsPlayer: true, sourceIsPlayer: true);
        runtime.Remove(301, tick: 12);                // 0.8s = 12 ticks
        Assert.Equal(StatusApplyResult.Ignored,
            runtime.Apply(thunder, 2, 10002, 8, tick: 15, targetIsPlayer: true, sourceIsPlayer: true)); // 0.5s 免疫

        runtime.Apply(wind, 1, 10002, 7, 0, targetIsPlayer: true, sourceIsPlayer: true);
        Assert.Equal(StatusApplyResult.Ignored,
            runtime.Apply(wind, 2, 10002, 8, tick: 30, targetIsPlayer: true, sourceIsPlayer: true)); // 4s 内不刷新
    }

    [Fact]
    public void Apply_Different_Effect_In_Same_Group_Replaces_Existing()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        var strongerBurn = new StatusEffectDefinition
        {
            EffectId = 202, EffectKind = 2, StackGroupId = 2,
            DurationSeconds = 3f, IntervalSeconds = 1f, DamagePerTick = 10f
        };
        var runtime = new StatusRuntime();
        runtime.Configure(catalog.Pvp);

        Assert.Equal(StatusApplyResult.Applied,
            runtime.Apply(burn, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true));
        Assert.Equal(StatusApplyResult.Applied,
            runtime.Apply(strongerBurn, 2, 10002, 8, tick: 5, targetIsPlayer: false, sourceIsPlayer: true));

        var active = Assert.Single(runtime.Active);
        Assert.Equal(202, active.EffectId);
        Assert.Equal(2L, active.SourceId);
        Assert.Equal(50u, active.EndTick);            // 5 + 45
        Assert.Equal(20u, active.NextTick);           // 5 + 15
    }

    [Fact]
    public void ClearAll_Removes_Statuses_And_Control_Immunity()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var wind = catalog.Statuses.Find(s => s.EffectId == 401)!;
        var thunder = catalog.Statuses.Find(s => s.EffectId == 301)!;
        var runtime = new StatusRuntime();
        runtime.Configure(catalog.Pvp);

        runtime.Apply(wind, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);
        runtime.Apply(thunder, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);
        runtime.Remove(301, tick: 12);
        Assert.True(runtime.ControlImmuneUntilTick > 0u);

        runtime.ClearAll();

        Assert.Equal(0, runtime.Count);
        Assert.Empty(runtime.Active);
        Assert.Equal(0u, runtime.ControlImmuneUntilTick);
        Assert.Equal(1f, runtime.IncomingDamageMultiplier(pvp: false, pvpMultiplier: 1.28f), 4);
        runtime.DeriveControl(out float speed, out bool locked);
        Assert.Equal(1f, speed, 4);
        Assert.False(locked);
    }

    [Fact]
    public void SecondsToTicks_Rounds_Up_At_15Hz()
    {
        Assert.Equal(30u, StatusRuntime.SecondsToTicks(2f));
        Assert.Equal(45u, StatusRuntime.SecondsToTicks(3f));
        Assert.Equal(12u, StatusRuntime.SecondsToTicks(0.8f));
        Assert.Equal(23u, StatusRuntime.SecondsToTicks(1.5f));
        Assert.Equal(8u, StatusRuntime.SecondsToTicks(0.5f));
        Assert.Equal(0u, StatusRuntime.SecondsToTicks(0f));
        Assert.Equal(0u, StatusRuntime.SecondsToTicks(float.NaN));
    }
}

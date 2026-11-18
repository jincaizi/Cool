using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class ServerStatusManagerTests
{
    private static PlayerState NewPlayer(ElementalCatalogDefinition catalog, long id = 1)
    {
        var player = new PlayerState(id);
        player.ConfigureStatuses(catalog);
        return player;
    }

    [Fact]
    public void Apply_Slow_Syncs_Control_And_Bumps_Combat_Version()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var player = NewPlayer(catalog);
        var slow = catalog.Statuses.Find(s => s.EffectId == 101)!;
        uint controlVersionBefore = player.ControlVersion;
        uint combatVersionBefore = player.CombatVersion;
        player.CombatDirty = false;

        var result = player.Statuses!.Apply(slow, sourceId: 2, skillId: 10002,
            castId: 7, tick: 0, catalog);

        Assert.Equal(StatusApplyResult.Applied, result);
        Assert.Equal(0.7f, player.ControlSpeedScale, 4);
        Assert.False(player.ControlActionLock);
        Assert.True(player.ControlVersion > controlVersionBefore);
        Assert.True(player.CombatVersion > combatVersionBefore);
        Assert.True(player.CombatDirty);
    }

    [Fact]
    public void Apply_Paralyze_Locks_Action_Without_Slowing()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var player = NewPlayer(catalog);
        var paralyze = catalog.Statuses.Find(s => s.EffectId == 301)!;

        player.Statuses!.Apply(paralyze, sourceId: 2, skillId: 10002, castId: 7, tick: 0, catalog);

        Assert.Equal(1f, player.ControlSpeedScale, 4);
        Assert.True(player.ControlActionLock);
        Assert.True(player.CombatDirty);
    }

    [Fact]
    public void Expiry_Restores_Control_And_Bumps_Version()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var player = NewPlayer(catalog);
        var slow = catalog.Statuses.Find(s => s.EffectId == 101)!;
        player.Statuses!.Apply(slow, 2, 10002, 7, tick: 0, catalog);
        uint controlVersionAfterApply = player.ControlVersion;
        player.CombatDirty = false;

        player.Statuses.RemoveExpired(30);   // 2s = 30 ticks，[0,30) 区间

        Assert.Equal(0, player.Statuses.Runtime.Count);
        Assert.Equal(1f, player.ControlSpeedScale, 4);
        Assert.False(player.ControlActionLock);
        Assert.True(player.ControlVersion > controlVersionAfterApply);
        Assert.True(player.CombatDirty);
    }

    [Fact]
    public void ClearAll_Removes_Statuses_And_Restores_Control()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var player = NewPlayer(catalog);
        var slow = catalog.Statuses.Find(s => s.EffectId == 101)!;
        var paralyze = catalog.Statuses.Find(s => s.EffectId == 301)!;
        player.Statuses!.Apply(slow, 2, 10002, 7, tick: 0, catalog);
        player.Statuses.Apply(paralyze, 2, 10002, 8, tick: 0, catalog);
        uint controlVersionBeforeClear = player.ControlVersion;
        player.CombatDirty = false;

        player.Statuses.ClearAll(10);

        Assert.Equal(0, player.Statuses.Runtime.Count);
        Assert.Equal(1f, player.ControlSpeedScale, 4);
        Assert.False(player.ControlActionLock);
        Assert.True(player.ControlVersion > controlVersionBeforeClear);
        Assert.True(player.CombatDirty);
    }

    [Fact]
    public void Projection_Carries_Active_Status_Fields()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var player = NewPlayer(catalog);
        var slow = catalog.Statuses.Find(s => s.EffectId == 101)!;
        player.Statuses!.Apply(slow, sourceId: 42, skillId: 10002, castId: 7, tick: 5, catalog);

        var projection = CombatProjectionBuilder.Build(player, catalog);

        var status = Assert.Single(projection.Statuses);
        Assert.Equal(101, status.EffectId);
        Assert.Equal(1, status.GroupId);
        Assert.Equal(35u, status.EndTick);       // 5 + 30
        Assert.Equal(42L, status.SourceId);
        Assert.Equal(0.3f, status.SlowRatio, 4);
    }

    [Fact]
    public void ConfigureStatuses_Is_Idempotent_And_Null_Until_Configured()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var player = new PlayerState(1);
        Assert.Null(player.Statuses);

        player.ConfigureStatuses(catalog);
        var first = player.Statuses;

        player.ConfigureStatuses(catalog);
        Assert.Same(first, player.Statuses);
    }

    [Fact]
    public void AllocateEventSeq_Is_Monotonic_Per_Target()
    {
        var player = new PlayerState(1);

        Assert.Equal(1u, player.AllocateEventSeq());
        Assert.Equal(2u, player.AllocateEventSeq());
        Assert.Equal(2u, player.NextEventSeq);
    }
}

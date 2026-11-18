using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CombatProjectionBuilderTests
{
    [Fact]
    public void Build_Carries_Authoritative_State_And_Equipment()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "P1");
        player.EquippedWeaponInstanceId = 42;
        player.EquippedWeaponTemplateId = ElementalCatalogDefaults.IceSwordTemplateId;
        player.EquipmentRevision = 3;
        player.CombatVersion = 7;
        player.Hp = 600f;
        player.MaxHp = 720f;
        player.IsDefending = true;

        var projection = CombatProjectionBuilder.Build(player, ElementalCatalogDefaults.Create());

        Assert.Equal(7u, projection.CombatVersion);
        Assert.Equal(1u, projection.LifeId);
        Assert.Equal(600f, projection.Hp);
        Assert.True(projection.IsDefending);
        Assert.Equal(42L, projection.Equipment!.WeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.IceSwordTemplateId, projection.Equipment.TemplateId);
        Assert.Equal(1, projection.Equipment.ElementId);
        Assert.Empty(projection.Statuses);
        Assert.Equal(1f, projection.Control!.SpeedScale);
    }

    [Fact]
    public void Projection_Carries_Authoritative_Control_State()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "P1");
        player.ApplyControl(0.7f, true);

        var projection = CombatProjectionBuilder.Build(player, ElementalCatalogDefaults.Create());

        Assert.NotNull(projection.Control);
        Assert.Equal(0.7f, projection.Control!.SpeedScale, 4);
        Assert.True(projection.Control.ActionLock);
        Assert.Equal(player.ControlVersion, projection.Control.ControlVersion);
    }

    [Fact]
    public void Projection_Carries_Active_Statuses_With_Definition_Fields()
    {
        var catalog = ElementalCatalogDefaults.Create();
        var store = new EntityStore();
        var player = store.AddPlayer(1, "P1");
        player.ConfigureStatuses(catalog);
        var burn = catalog.Statuses.Find(s => s.EffectId == 201)!;
        var wind = catalog.Statuses.Find(s => s.EffectId == 401)!;
        player.Statuses!.Apply(burn, sourceId: 2, skillId: 10002, castId: 7, tick: 0, catalog);
        player.Statuses.Apply(wind, sourceId: 3, skillId: 10002, castId: 8, tick: 0, catalog);

        var projection = CombatProjectionBuilder.Build(player, catalog);

        Assert.Equal(2, projection.Statuses.Count);
        var burnProjection = projection.Statuses.Single(s => s.EffectId == 201);
        Assert.Equal(2, burnProjection.GroupId);
        Assert.Equal(45u, burnProjection.EndTick);       // 3s = 45 ticks
        Assert.Equal(2L, burnProjection.SourceId);
        Assert.Equal(8f, burnProjection.DamagePerTick);
        Assert.Equal(15u, burnProjection.IntervalTicks); // 1s = 15 ticks
        var windProjection = projection.Statuses.Single(s => s.EffectId == 401);
        Assert.Equal(2f, windProjection.IncomingDamageMultiplier);
    }
}

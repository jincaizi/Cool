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
}

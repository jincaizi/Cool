using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class LoginCombatBaselineTests
{
    [Fact]
    public void Registered_Player_Uses_Configured_Map_Spawn()
    {
        var spawn = new Vector3(-15f, 0.3f, -30f);
        using var server = new Server3C(new TestLogger(), spawn);

        var player = server.RegisterEntity(7, "SpawnBaseline", features:
            ProtocolFeatures.ElementalSwordQiV1 | ProtocolFeatures.PvpMovementBudgetV1);

        Assert.Equal(spawn, player.Position);
        Assert.Equal(0L, player.LastUpdateTimestamp);
    }

    [Fact]
    public void Elemental_Session_Gets_720_Hp_And_Strict_Budget()
    {
        var player = new PlayerState(1);
        LoginHandler.ApplyLoginCombatBaseline(player, ElementalCatalogDefaults.Create(),
            ProtocolFeatures.ElementalSwordQiV1 | ProtocolFeatures.PvpMovementBudgetV1);

        Assert.Equal(720f, player.MaxHp);
        Assert.Equal(720f, player.Hp);
        Assert.True(player.StrictMovementBudget);
    }

    [Fact]
    public void Legacy_Session_Keeps_100_Hp_And_Old_Validation()
    {
        var player = new PlayerState(1);
        LoginHandler.ApplyLoginCombatBaseline(player, ElementalCatalogDefaults.Create(), 0);

        Assert.Equal(100f, player.MaxHp);
        Assert.False(player.StrictMovementBudget);
    }
}

using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class PlayerControlStateTests
{
    [Fact]
    public void ApplyControl_Clamps_And_Bumps_Version_Only_On_Change()
    {
        var player = new PlayerState(1);

        player.ApplyControl(0.7f, false);
        Assert.Equal(0.7f, player.ControlSpeedScale, 4);
        Assert.Equal(1u, player.ControlVersion);

        player.ApplyControl(0.7f, false);
        Assert.Equal(1u, player.ControlVersion);

        player.ApplyControl(float.NaN, true);
        Assert.Equal(0f, player.ControlSpeedScale);
        Assert.True(player.ControlActionLock);
        Assert.Equal(2u, player.ControlVersion);
    }

    [Fact]
    public void MarkDead_Resets_Control_But_Keeps_Death_Version_Advance()
    {
        var player = new PlayerState(1);
        player.ApplyControl(0f, true);
        var before = player.CombatVersion;

        player.MarkDead(10);

        Assert.Equal(1f, player.ControlSpeedScale);
        Assert.False(player.ControlActionLock);
        Assert.True(player.CombatVersion > before);
    }

    [Fact]
    public void SetMaxHp_Fills_Login_Health()
    {
        var player = new PlayerState(1);

        player.SetMaxHp(720f);

        Assert.Equal(720f, player.MaxHp);
        Assert.Equal(720f, player.Hp);
    }
}

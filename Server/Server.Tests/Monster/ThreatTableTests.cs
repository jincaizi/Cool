using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class ThreatTableTests
{
    [Fact]
    public void AddDamage_Accumulates_Per_Player_With_Flat_And_Ratio()
    {
        var table = new ThreatTable();

        table.AddDamage(1, 10f);
        table.AddDamage(1, 5f);
        table.AddDamage(2, 20f);

        Assert.Equal(35f, table.ThreatOf(1), 3); // (10+10) + (5+10)
        Assert.Equal(30f, table.ThreatOf(2), 3); // 20 + 10
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void Initial_Threat_Is_Flat()
    {
        var table = new ThreatTable();
        table.AddInitial(7);
        Assert.Equal(ThreatTable.ThreatInitial, table.ThreatOf(7), 3);
    }

    [Fact]
    public void Highest_Breaks_Ties_By_Smaller_Player_Id()
    {
        var table = new ThreatTable();
        table.Add(2, 10f);
        table.Add(1, 10f);
        table.Add(3, 5f);

        Assert.Equal(1L, table.Highest(_ => true));
    }

    [Fact]
    public void Highest_Skips_Invalid_Entries()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Add(2, 10f);

        Assert.Equal(2L, table.Highest(id => id != 1));
    }

    [Fact]
    public void Selector_Keeps_Current_Inside_Switch_Threshold()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Add(2, 110f); // 只高 10%，低于 20% 滞后

        Assert.Equal(1L, TargetSelector.Select(table, currentTarget: 1, _ => true));
    }

    [Fact]
    public void Selector_Switches_When_New_Target_Exceeds_Threshold()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Add(2, 130f); // 高 30%

        Assert.Equal(2L, TargetSelector.Select(table, currentTarget: 1, _ => true));
    }

    [Fact]
    public void Selector_Taunt_Overrides_Threat_While_Valid()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Ensure(2);

        Assert.Equal(2L, TargetSelector.Select(table, currentTarget: 1, _ => true, tauntTarget: 2));
    }

    [Fact]
    public void Selector_Falls_To_Highest_When_Current_Invalid()
    {
        var table = new ThreatTable();
        table.Add(1, 100f);
        table.Add(2, 50f);

        Assert.Equal(2L, TargetSelector.Select(table, currentTarget: 1, id => id != 1));
    }
}

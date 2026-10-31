using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class LoginRosterTests
{
    [Fact]
    public void Roster_Contains_Existing_Players_Except_The_New_One()
    {
        var states = new Dictionary<long, PlayerState>
        {
            [1] = new PlayerState(1) { PlayerName = "A" },
            [2] = new PlayerState(2) { PlayerName = "B" },
            [3] = new PlayerState(3) { PlayerName = "New" }
        };

        var roster = LoginHandler.BuildRoster(states, excludePlayerId: 3L, zoneId: "Global");

        Assert.Equal(new[] { 1L, 2L }, roster.Select(r => r.PlayerId).OrderBy(id => id).ToArray());
        Assert.Equal("A", roster.Single(r => r.PlayerId == 1L).PlayerName);
        Assert.Equal("B", roster.Single(r => r.PlayerId == 2L).PlayerName);
    }
}

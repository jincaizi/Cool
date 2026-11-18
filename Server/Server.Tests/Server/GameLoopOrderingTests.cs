using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KcpServer.Tests;

public class GameLoopOrderingTests
{
    private sealed class FakePacket
    {
    }

    [Fact]
    public async Task PreTick_Runs_Before_Packet_And_OnTick_Runs_After()
    {
        var order = new List<string>();
        var done = new ManualResetEventSlim();
        var loop = new GameLoop<FakePacket>(TimeSpan.FromMilliseconds(200),
            _ => order.Add("packet"), new TestLogger(),
            onTick: tick =>
            {
                order.Add("post:" + tick);
                if (tick == 1) done.Set();
            },
            onPreTick: tick => order.Add("pre:" + tick));

        loop.Enqueue(new FakePacket());   // 启动前入队：首个 tick 的同一迭代内处理
        loop.Start();
        try
        {
            Assert.True(done.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(new[] { "pre:1", "packet", "post:1" }, order);
        }
        finally
        {
            await loop.Stop();
        }
    }

    [Fact]
    public async Task TickCount_Advances_Only_After_PreTick_And_Packet()
    {
        long preCounter = -1, packetCounter = -1, postCounter = -1;
        long preTick = -1, postTick = -1;
        var done = new ManualResetEventSlim();
        GameLoop<FakePacket>? loop = null;
        loop = new GameLoop<FakePacket>(TimeSpan.FromMilliseconds(200),
            _ => packetCounter = loop!.TickCount, new TestLogger(),
            onTick: tick =>
            {
                postCounter = loop!.TickCount;
                postTick = tick;
                if (tick == 1) done.Set();
            },
            onPreTick: tick =>
            {
                if (tick != 1) return;
                preCounter = loop!.TickCount;
                preTick = tick;
            });

        loop.Enqueue(new FakePacket());
        loop.Start();
        try
        {
            Assert.True(done.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(1L, preTick);
            Assert.Equal(0L, preCounter);      // 预 tick 时 TickCount 仍是上一 tick
            Assert.Equal(0L, packetCounter);   // 包处理同样看到上一 tick 计数
            Assert.Equal(1L, postTick);
            Assert.Equal(1L, postCounter);     // onTick 时已推进到本 tick
        }
        finally
        {
            await loop.Stop();
        }
    }

    [Fact]
    public async Task Empty_Queue_Still_Runs_PreTick_And_OnTick_With_Same_Tick()
    {
        long preTick = -1, postTick = -1;
        var done = new ManualResetEventSlim();
        var loop = new GameLoop<FakePacket>(TimeSpan.FromMilliseconds(200),
            _ => { }, new TestLogger(),
            onTick: tick =>
            {
                if (tick != 1) return;
                postTick = tick;
                done.Set();
            },
            onPreTick: tick =>
            {
                if (tick == 1) preTick = tick;
            });

        loop.Start();
        try
        {
            Assert.True(done.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(1L, preTick);
            Assert.Equal(1L, postTick);
            Assert.Equal(1L, loop.TickCount);
        }
        finally
        {
            await loop.Stop();
        }
    }
}

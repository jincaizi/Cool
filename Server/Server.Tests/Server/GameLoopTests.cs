using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KcpServer.Tests;

public class GameLoopTests
{
    private sealed class FakePacket
    {
        public int Value;
    }

    [Fact]
    public void Loop_Processes_Enqueued_Packets_In_Order()
    {
        var processed = new List<int>();
        var loop = new GameLoop<FakePacket>(TimeSpan.FromMilliseconds(5),
            packet => processed.Add(packet.Value), new TestLogger());

        loop.Start();
        try
        {
            for (int i = 0; i < 5; i++) loop.Enqueue(new FakePacket { Value = i });

            SpinWait.SpinUntil(() => processed.Count >= 5, TimeSpan.FromSeconds(2));

            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, processed);
        }
        finally
        {
            loop.Stop();
        }
    }

    [Fact]
    public void Loop_Throttles_To_Interval()
    {
        // 节流可观测量是 TickCount（handler 仅按包触发，空队列下不调用）
        var loop = new GameLoop<FakePacket>(TimeSpan.FromMilliseconds(20),
            _ => { }, new TestLogger());

        loop.Start();
        try
        {
            Thread.Sleep(250);
            long ticks = loop.TickCount;
            // 250ms / 20ms ≈ 12 次；断言在 [5, 25] 区间（宽松，防慢机器误报）
            Assert.InRange(ticks, 5, 25);
        }
        finally
        {
            loop.Stop();
        }
    }

    [Fact]
    public async Task Stop_Completes_Without_Throwing()
    {
        // 回归：取消信号到达 Task.Delay 时 Stop 必须干净完成（否则 Server3C.StopAsync 每次停机都会抛 TaskCanceledException）
        var processed = 0;
        var loop = new GameLoop<FakePacket>(TimeSpan.FromMilliseconds(5),
            packet => Interlocked.Increment(ref processed), new TestLogger());

        loop.Start();
        try
        {
            for (int i = 0; i < 5; i++) loop.Enqueue(new FakePacket());
            SpinWait.SpinUntil(() => processed >= 5, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await loop.Stop();
        }

        // Stop 返回后循环已终止：新入队不再被处理
        long before = loop.TickCount;
        loop.Enqueue(new FakePacket());
        await Task.Delay(30);
        Assert.Equal(before, loop.TickCount);
        Assert.Equal(5, processed);
    }
}

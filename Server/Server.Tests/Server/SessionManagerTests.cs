using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class SessionManagerTests : IDisposable
{
    private readonly SessionManager _manager;
    private readonly TestLogger _logger = new TestLogger();

    public SessionManagerTests()
    {
        _manager = new SessionManager(_logger);
    }

    public void Dispose() => _manager.Dispose();

    [Fact]
    public void Concurrent_FirstPackets_SameEndpoint_CreateOneSession()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 40001);
        var firstPacket = BuildFirstPacket(conv: 42);

        // 模拟 IO 线程并发首包（P0 MUST-DO-3 竞态场景）
        var tasks = new List<Task>();
        var sessions = new List<KcpServerSession>();
        for (int i = 0; i < 16; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var s = _manager.GetOrCreateSession(endpoint, firstPacket);
                lock (sessions) sessions.Add(s);
            }));
        }
        Task.WaitAll(tasks.ToArray());

        Assert.Equal(1, _manager.SessionCount);
    }

    [Fact]
    public void Session_Adopts_Conv_From_First_Packet()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 40002);
        var session = _manager.GetOrCreateSession(endpoint, BuildFirstPacket(conv: 12345));
        Assert.NotNull(session);
        Assert.Equal(12345U, session.KcpConv);
    }

    [Fact]
    public void Remove_By_Endpoint_Unregisters_Session()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 40003);
        var session = _manager.GetOrCreateSession(endpoint, BuildFirstPacket(conv: 1));
        Assert.Equal(1, _manager.SessionCount);

        _manager.RemoveByEndpoint(endpoint);
        Assert.Equal(0, _manager.SessionCount);
        Assert.Null(_manager.FindByEndpoint(endpoint));
    }

    [Fact]
    public void Shutdown_Closes_All_Sessions()
    {
        for (int i = 0; i < 3; i++)
        {
            _manager.GetOrCreateSession(new IPEndPoint(IPAddress.Loopback, 40010 + i), BuildFirstPacket(conv: (uint)(i + 1)));
        }
        Assert.Equal(3, _manager.SessionCount);

        _manager.ShutdownAll("TestShutdown").GetAwaiter().GetResult();
        Assert.Equal(0, _manager.SessionCount);
    }

    private static byte[] BuildFirstPacket(uint conv)
    {
        // KCP 头前 4 字节 = conv（LE），≥24 字节才被会话创建逻辑采纳
        var data = new byte[24];
        BitConverter.GetBytes(conv).CopyTo(data, 0);
        return data;
    }
}

public sealed class TestLogger : ILogger
{
    public bool IsEnabled(LogLevel level) => true;
    public void Log(LogLevel level, Exception? exception, string message, params object[] args) { }
    public void LogDebug(string message, params object[] args) { }
    public void LogInformation(string message, params object[] args) { }
    public void LogWarning(string message, params object[] args) { }
    public void LogError(string message, params object[] args) { }
    public void LogError(Exception ex, string message, params object[] args) { }
    public void LogCritical(string message, params object[] args) { }
    public void LogCritical(Exception ex, string message, params object[] args) { }
}

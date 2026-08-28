using System;
using System.Net;
using System.Threading.Tasks;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// 登录版本拒绝路径行为链测试（T9 审查回归：37/37 无覆盖）。
/// 行为链：ProtocolVersion 不匹配 → reject LoginResponse + Kick + flush 窗口（50ms）→ CloseAsync。
/// 真实 session（SessionManager 创建，同 SessionManagerTests 无真 socket 模式）：
/// Server3C 仅构造不 StartAsync（_udpServer==null → SendToSessionAsync 跳过实际发送），
/// update loop 的 Output 在无发送 socket 时记警告不崩溃；可观察断言点 =
/// HandleLoginAsync 无异常 + 会话被关闭（IsConnected=false）。
/// </summary>
public class VersionRejectTests : IDisposable
{
    private readonly TestLogger _logger = new TestLogger();
    private readonly SessionManager _sessionManager;
    private readonly Server3C _server;
    private readonly LoginHandler _loginHandler;

    public VersionRejectTests()
    {
        _sessionManager = new SessionManager(_logger);
        _server = new Server3C(_logger); // 仅构造，不 StartAsync（不开 socket）
        _loginHandler = new LoginHandler(_server, _server.RoomManager, _logger);
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task VersionMismatch_RejectPath_ClosesSession()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 41001);
        var session = _sessionManager.GetOrCreateSession(endpoint, BuildFirstPacket(conv: 7));

        var request = new LoginRequest
        {
            Username = "mismatch-client",
            ProtocolVersion = 0 // != ProtocolConstants.ProtocolVersion (1)
        };

        await _loginHandler.HandleLoginAsync(session, request); // 不应抛异常

        // CloseAsync 已执行：会话状态离开 Connected（否则将挂到 300s 超时）
        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task VersionMatch_Path_KeepsSessionConnected()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 41002);
        var session = _sessionManager.GetOrCreateSession(endpoint, BuildFirstPacket(conv: 8));

        var request = new LoginRequest
        {
            Username = "ok-client",
            ProtocolVersion = ProtocolConstants.ProtocolVersion
        };

        await _loginHandler.HandleLoginAsync(session, request);

        // 对照组：版本匹配路径不关闭会话（关闭动作仅来自版本拒绝分支）
        Assert.True(session.IsConnected);
    }

    private static byte[] BuildFirstPacket(uint conv)
    {
        // KCP 头前 4 字节 = conv（LE），≥24 字节才被会话创建逻辑采纳
        var data = new byte[24];
        BitConverter.GetBytes(conv).CopyTo(data, 0);
        return data;
    }
}

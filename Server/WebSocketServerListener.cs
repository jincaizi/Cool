using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// WebSocket 监听器 —— 与 UDP/KCP 同进程并存的第二条传输入口（D-W3）。
    /// 每接受一个连接就建 WebSocketServerSession，并交给 Server3C.RegisterSession 接进事件链；
    /// 之后它触发的 DataReceived 会自动汇入既有的 ProcessPacket，业务侧零改动。
    /// </summary>
    public sealed class WebSocketServerListener : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Server3C _server;
        private readonly ILogger _logger;
        private readonly Func<long> _nextSessionId;
        private CancellationTokenSource? _cts;

        public WebSocketServerListener(Server3C server, ILogger logger, Func<long> nextSessionId)
        {
            _server = server;
            _logger = logger;
            _nextSessionId = nextSessionId;
        }

        /// <summary>prefix 形如 "http://127.0.0.1:8000/"（本机开发）或 "http://+:8000/"（对外，需 URL ACL，见 Task 9）</summary>
        public void Start(string prefix)
        {
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
            _logger.LogInformation($"WebSocket listener started on {prefix}");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (_listener.IsListening && !ct.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }   // 监听器已停

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;   // 只服务 WS，不服务 HTTP
                    context.Response.Close();
                    continue;
                }

                try
                {
                    var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                    var session = new WebSocketServerSession(_nextSessionId(), wsContext.WebSocket, _logger);
                    _server.RegisterSession(session);     // ← Task 2 Step 6 的接线点
                    _logger.LogInformation($"WebSocket session {session.SessionId} accepted");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WebSocket accept failed");
                }
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); _listener.Stop(); _listener.Close(); } catch { }
            _cts?.Dispose();
        }
    }
}

using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// 只回吐字节的 WS 端点：收到什么就 SendAsync 回什么。
    /// 用途是**单独**验证 WebGL 的 jslib 通道——它不理解任何游戏协议，
    /// 所以判据是"字节双向流动"，不是"能进世界"（后者由 Task 5 的真监听验收）。
    ///
    /// 刻意不做分片聚合：这里回的是字节不是消息，逐块回吐正好是对的。
    /// </summary>
    public sealed class WebSocketEchoListener : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly ILogger _logger;

        public WebSocketEchoListener(ILogger logger) { _logger = logger; }

        public void Start(string prefix)
        {
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _logger.LogInformation($"WS echo listening on {prefix}");
            _ = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }             // 监听已停
                _ = EchoAsync(context);
            }
        }

        private async Task EchoAsync(HttpListenerContext context)
        {
            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            WebSocket socket;
            try { socket = (await context.AcceptWebSocketAsync(null)).WebSocket; }
            catch (Exception ex)
            {
                _logger.LogWarning($"WS echo handshake failed: {ex.Message}");
                return;
            }

            var buffer = new byte[16 * 1024];
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    await socket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count),
                        WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
                }
            }
            catch (Exception ex) { _logger.LogDebug($"WS echo ended: {ex.Message}"); }
            finally
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "echo done", CancellationToken.None); }
                catch (Exception) { /* 对端已经断了 */ }
                socket.Dispose();
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch (Exception) { /* 已停 */ }
        }
    }
}

#if !UNITY_WEBGL
using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketNet
{
    /// <summary>
    /// 非 WebGL 平台的通道实现（编辑器 / PC / 原生）。
    /// WebGL 下 System.Net.WebSockets 不可用，编译期整体排除——
    /// 与 KcpNet 的平台守卫同一手法（见 Task 1）。
    /// 注意：本类存在的意义是让"编辑器里能跑真 WS"，从而把 jslib 的验证范围缩到最小。
    /// </summary>
    public sealed class DotNetWebSocketChannel : IWebSocketChannel
    {
        private const int ReceiveBufferSize = 64 * 1024;

        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cts;

        public bool IsConnected => _socket?.State == WebSocketState.Open;

        public event Action<byte[]>? MessageReceived;
        public event Action<string>? Closed;
        public event Action<Exception>? Error;

        public async Task<bool> ConnectAsync(string uri)
        {
            _socket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            try
            {
                await _socket.ConnectAsync(new Uri(uri), _cts.Token);
                _ = ReceiveLoopAsync();   // fire-and-forget：接收循环独立于连接调用
                return IsConnected;
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                return false;
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[ReceiveBufferSize];
            var accumulated = new MemoryStream();

            try
            {
                while (_socket != null && _socket.State == WebSocketState.Open)
                {
                    var result = await _socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), _cts!.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Closed?.Invoke("closed by peer");
                        return;
                    }

                    accumulated.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;   // 分片：聚合到一帧完整消息

                    var frame = accumulated.ToArray();
                    accumulated.SetLength(0);
                    MessageReceived?.Invoke(frame);       // 一条消息一帧（D-W9）
                }
            }
            catch (OperationCanceledException) { /* 主动关闭，非错误 */ }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                Closed?.Invoke("receive loop terminated");
            }
        }

        public async Task SendAsync(byte[] data, int offset, int length)
        {
            if (_socket == null || _socket.State != WebSocketState.Open) return;
            await _socket.SendAsync(
                new ArraySegment<byte>(data, offset, length),
                WebSocketMessageType.Binary, endOfMessage: true, _cts!.Token);
        }

        public async Task CloseAsync(string reason)
        {
            try
            {
                if (_socket != null && _socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
            }
            catch { /* 关闭路径的异常不值得上抛 */ }
            finally { _cts?.Cancel(); }
        }

        public void Dispose()
        {
            try { _socket?.Dispose(); } catch { }
            _cts?.Dispose();
            _socket = null;
            _cts = null;
        }
    }
}
#endif

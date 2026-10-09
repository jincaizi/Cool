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
        private bool _closed;   // RaiseClosed 已触发的权威否决位（见 IsConnected / RaiseClosed）
        private int _generation;   // 每轮 ConnectAsync 递增——陈旧接收循环据此自我作废（见 ReceiveLoopAsync）

        public bool IsConnected => !_closed && _socket?.State == WebSocketState.Open;

        public event Action<byte[]>? MessageReceived;
        public event Action<string>? Closed;
        public event Action<Exception>? Error;

        public async Task<bool> ConnectAsync(string uri)
        {
            _socket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            _closed = false;   // 重连：清掉上一轮的否决位，否则这条通道永远报未连接
            int generation = ++_generation;   // 本轮代际——上一轮的接收循环从此无权再动本通道
            try
            {
                await _socket.ConnectAsync(new Uri(uri), _cts.Token);
                _ = ReceiveLoopAsync(generation);   // fire-and-forget：接收循环独立于连接调用
                return IsConnected;
            }
            catch (Exception ex)
            {
                InvokeConsumer(() => Error?.Invoke(ex));
                return false;
            }
        }

        private async Task ReceiveLoopAsync(int generation)
        {
            // socket / token 取成本地量：字段属于"当前连接"，而本循环只属于传入的那一代。
            // 否则 Dispose 后重连时，本循环会拿着**别人的** socket 继续读（详见 RaiseClosed）
            var socket = _socket;
            var token = _cts!.Token;
            var buffer = new byte[ReceiveBufferSize];
            var accumulated = new MemoryStream();

            try
            {
                while (generation == _generation && socket != null && socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), token);

                    // 等待期间通道被重连接管：本轮的消息与结论一概作废，不得惊动新一代
                    if (generation != _generation) return;
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        RaiseClosed(generation, "closed by peer");
                        return;
                    }

                    accumulated.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;   // 分片：聚合到一帧完整消息

                    var frame = accumulated.ToArray();
                    accumulated.SetLength(0);
                    // 一条消息一帧（D-W9）。消费者的异常不得终止本循环——否则 State 仍 Open
                    // 而无人再读，IsConnected 继续报 true，连接假活（ClientNetworkHost 明确预期
                    // 消费者回调会抛：它在该线程上读 Time.unscaledTime）
                    InvokeConsumer(() => MessageReceived?.Invoke(frame));
                }
            }
            catch (OperationCanceledException) { /* 主动关闭，非错误 */ }
            catch (Exception ex)
            {
                // 陈旧循环的异常属于上一代：它既不是新连接的错，更不得把它判死
                if (generation != _generation) return;
                // Error 与 Closed 各自独立受保护：Error 的消费者抛异常也挡不住 Closed
                InvokeConsumer(() => Error?.Invoke(ex));
                RaiseClosed(generation, "receive loop terminated");
            }
        }

        /// <summary>
        /// 终结接收并通知上层。顺序即不变量：
        /// ① 先置 <see cref="_closed"/>（权威否决位）——"Closed 触发时 IsConnected 必为 false"
        ///    由此**按构造成立**，不依赖 Abort 是否成功、也不依赖状态恰好如此；
        /// ② 再 Abort 让底层状态与之一致（释放 socket、使 SendAsync 的闸门也合上，
        ///    否则会留下一个无人再读却仍可写的 open socket）；
        /// ③ 最后抛 Closed（经 InvokeConsumer，消费者抛异常不影响 ①② 已落定）。
        /// generation 不是本代即直接返回：_closed 与 _socket 都属于"当前连接"，
        /// 陈旧循环不持有它们的处置权（否则 Connect → Dispose → Connect 的竞争里，
        /// 上一轮的收尾会把健康的新连接永久判死并 Abort 掉）。
        /// </summary>
        private void RaiseClosed(int generation, string reason)
        {
            if (generation != _generation) return;
            _closed = true;
            try { _socket?.Abort(); }
            catch { /* Abort 失败不应挡住通知——IsConnected 已由 _closed 否决 */ }
            InvokeConsumer(() => Closed?.Invoke(reason));
        }

        /// <summary>
        /// 调用消费者回调并吞掉消费者自身的异常——它只反映订阅者的 bug，既不得终止接收循环，
        /// 也不得挡住同一路径上的后续通知。仅留痕。
        /// </summary>
        private static void InvokeConsumer(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                try { UnityEngine.Debug.LogError("[WebSocketNet] consumer callback threw: " + ex); }
                catch { /* 日志通道不可用时也不中断 */ }
            }
        }

        public async Task SendAsync(byte[] data, int offset, int length)
        {
            // 闸门与 IsConnected 同源：_closed 一置，即便 Abort 未能改变 State 也不再放行
            if (_closed || _socket == null || _socket.State != WebSocketState.Open) return;
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

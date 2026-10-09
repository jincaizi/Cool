#if UNITY_WEBGL
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace WebSocketNet
{
    /// <summary>
    /// WebGL 平台通道 —— 经 .jslib 调用浏览器 WebSocket API。
    ///
    /// 与 DotNetWebSocketChannel 的关键区别：**没有后台接收循环**。浏览器的
    /// WS 回调在 JS 侧只把帧塞进队列，C# 每帧经 Pump() 主动取。这样跨界只有
    /// 一个方向（C# → JS），用不到 SendMessage / gameInstance / dynCall /
    /// base64 —— 那条方向最容易写错，且错了最难查。
    ///
    /// 平台守卫是必须的：本类引用 UnityEngine 且 .jslib 只在 WebGL 构建里存在。
    /// </summary>
    public sealed class WebGLWebSocketChannel : IWebSocketChannel
    {
        [DllImport("__Internal")] private static extern int WebSocketNet_Connect(string url);
        // [Out] 是保险：若运行时改用临时缓冲编组 byte[]，写了也必须拷回来。
        // （blittable 数组通常直接传托管内存指针，此时 [Out] 无额外开销）
        [DllImport("__Internal")] private static extern int WebSocketNet_Poll(int handle, [Out] byte[] dest, int maxLen);
        [DllImport("__Internal")] private static extern int WebSocketNet_Send(int handle, byte[] data, int length);
        [DllImport("__Internal")] private static extern int WebSocketNet_GetState(int handle);
        [DllImport("__Internal")] private static extern int WebSocketNet_GetCloseCode(int handle);
        [DllImport("__Internal")] private static extern int WebSocketNet_GetDropped(int handle);
        [DllImport("__Internal")] private static extern void WebSocketNet_Close(int handle);

        private const int StateOpen = 1;
        private const int StateClosed = 2;
        private const int StateFailed = 3;

        private const int InitialRecvBufferSize = 64 * 1024;
        private const int MaxRecvBufferSize = 1024 * 1024;
        private const float ConnectTimeoutSeconds = 5f;

        private byte[] _recvBuffer = new byte[InitialRecvBufferSize];
        private int _handle = -1;
        private bool _open;
        private bool _terminalRaised;
        private int _lastDropped;
        private float _connectDeadline;
        private TaskCompletionSource<bool>? _connectTcs;
        private WebGLWebSocketPump? _pump;

        public bool IsConnected => _open && _handle >= 0 && WebSocketNet_GetState(_handle) == StateOpen;

        public event Action<byte[]>? MessageReceived;
        public event Action<string>? Closed;
        public event Action<Exception>? Error;

        public WebGLWebSocketChannel()
        {
            var go = new GameObject("WebGLWebSocketPump");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _pump = go.AddComponent<WebGLWebSocketPump>();
            _pump.Target = this;
        }

        public Task<bool> ConnectAsync(string uri)
        {
            _handle = WebSocketNet_Connect(uri);
            if (_handle < 0)
            {
                Error?.Invoke(new Exception("Failed to create browser WebSocket: " + uri));
                return Task.FromResult(false);
            }
            _connectTcs = new TaskCompletionSource<bool>();
            _connectDeadline = Time.realtimeSinceStartup + ConnectTimeoutSeconds;
            // 这个 Task 由 Pump() 完成 —— 不用 Task.Delay，不依赖 WebGL 的
            // SynchronizationContext（那条路我没验证过，而它一旦不成立就是"什么都
            // 没发生"）
            return _connectTcs.Task;
        }

        /// <summary>由 WebGLWebSocketPump 每帧调用一次。</summary>
        internal void Pump()
        {
            int handle = _handle;
            if (handle < 0) return;

            int state = WebSocketNet_GetState(handle);

            if (!_open && state == StateOpen)
            {
                _open = true;
                _connectTcs?.TrySetResult(true);
                _connectTcs = null;
            }

            while (true)
            {
                int n = WebSocketNet_Poll(handle, _recvBuffer, _recvBuffer.Length);
                if (n > 0)
                {
                    var frame = new byte[n];
                    Buffer.BlockCopy(_recvBuffer, 0, frame, 0, n);
                    MessageReceived?.Invoke(frame);
                    continue;      // 订阅者可能在这期间 CloseAsync —— 下一轮 Poll 会返回 0
                }
                if (n == -1)
                {
                    // 单帧比缓冲还大：翻倍重试；到顶就当错误，避免死循环
                    if (_recvBuffer.Length >= MaxRecvBufferSize)
                    {
                        Error?.Invoke(new Exception("WebSocket frame exceeds " + MaxRecvBufferSize + " bytes"));
                        break;
                    }
                    _recvBuffer = new byte[Math.Min(_recvBuffer.Length * 2, MaxRecvBufferSize)];
                    continue;
                }
                break;             // n == 0，队列已空
            }

            int dropped = WebSocketNet_GetDropped(handle);
            if (dropped != _lastDropped)
            {
                _lastDropped = dropped;
                Debug.LogWarning("[WebSocketNet] dropped " + dropped + " frame(s) - the pump fell behind");
            }

            if (state == StateClosed || state == StateFailed)
            {
                _open = false;
                if (_terminalRaised) return;
                _terminalRaised = true;
                _connectTcs?.TrySetResult(false);
                _connectTcs = null;
                if (state == StateFailed) Error?.Invoke(new Exception("WebSocket failed in browser"));
                Closed?.Invoke("close:" + WebSocketNet_GetCloseCode(handle));
                return;
            }

            if (!_open && _connectTcs != null && Time.realtimeSinceStartup >= _connectDeadline)
            {
                _connectTcs.TrySetResult(false);
                _connectTcs = null;
                Error?.Invoke(new Exception("WebSocket open timed out: " + ConnectTimeoutSeconds + "s"));
            }
        }

        public Task SendAsync(byte[] data, int offset, int length)
        {
            if (!IsConnected) return Task.CompletedTask;   // 与 KcpNetClientAdapter 同语义：未连接静默丢弃
            if (offset != 0)
            {
                // offset 非 0 时先切片 —— jslib 只接受整数组 + 长度
                var slice = new byte[length];
                Array.Copy(data, offset, slice, 0, length);
                WebSocketNet_Send(_handle, slice, length);
            }
            else
            {
                WebSocketNet_Send(_handle, data, length);
            }
            return Task.CompletedTask;
        }

        public Task CloseAsync(string reason)
        {
            if (_handle >= 0)
            {
                WebSocketNet_Close(_handle);
                _handle = -1;
            }
            _open = false;
            _terminalRaised = true;
            _connectTcs?.TrySetResult(false);
            _connectTcs = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            CloseAsync("dispose").GetAwaiter().GetResult();
            if (_pump != null)
            {
                UnityEngine.Object.Destroy(_pump.gameObject);
                _pump = null;
            }
        }
    }
}
#endif

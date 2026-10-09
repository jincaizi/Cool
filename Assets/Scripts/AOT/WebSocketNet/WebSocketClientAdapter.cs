using System;
using System.Threading.Tasks;
using Cool.Protocol;

namespace WebSocketNet
{
    /// <summary>
    /// INetworkClient 的 WebSocket 实现 —— 与 KcpNetClientAdapter 同构：
    /// 上层（ClientNetworkManager / CombatNetworkBridge）只认接口，换传输零改动。
    /// 一条消息 = 一个 WS 帧（D-W9），直接复用 MessageCodec，无需再拆帧。
    /// 通道可注入（D-W8）——测试注假通道，运行时注 DotNet/WebGL 通道。
    /// </summary>
    public sealed class WebSocketClientAdapter : INetworkClient
    {
        private readonly IWebSocketChannel _channel;

        public bool IsConnected => _channel.IsConnected;

        public event Action<MessageId, IMessage>? MessageReceived;
        public event Action<string>? Disconnected;
        public event Action<Exception>? Error;

        public WebSocketClientAdapter(IWebSocketChannel channel)
        {
            _channel = channel;
            _channel.MessageReceived += OnFrame;
            _channel.Closed += OnClosed;
            _channel.Error += OnError;
        }

        public async Task<bool> ConnectAsync(string host, int port)
        {
            try
            {
                // 演示形态下走明文 ws:// —— wss 由部署层的反向代理终止（Task 9）
                return await _channel.ConnectAsync("ws://" + host + ":" + port + "/");
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                return false;
            }
        }

        public Task DisconnectAsync(string reason) => _channel.CloseAsync(reason);

        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
        {
            if (!_channel.IsConnected) return;   // 与 KcpNetClientAdapter 同语义：未连接静默丢弃

            byte[] frame = MessageCodec.EncodeToArray(messageId, message, flags);
            // fire-and-forget：不在调用线程等待（与 KcpNetClientAdapter 一致，避免主线程死锁）
            _ = _channel.SendAsync(frame, 0, frame.Length);
        }

        private void OnFrame(byte[] data)
        {
            if (!MessageCodec.TryDecode(data, out var decoded, out var decodeError))
            {
                // 畸形帧留痕但不打断接收循环——静默吞掉会让排查无线索。
                // 原因必须带上（inner）：只有"解码失败"这四个字，等于回到无线索
                Error?.Invoke(new InvalidOperationException("WebSocket frame failed to decode", decodeError));
                return;
            }
            MessageReceived?.Invoke(decoded.messageId, decoded.message);
        }

        private void OnClosed(string reason) => Disconnected?.Invoke(reason);

        private void OnError(Exception ex) => Error?.Invoke(ex);

        public void Dispose()
        {
            _channel.MessageReceived -= OnFrame;
            _channel.Closed -= OnClosed;
            _channel.Error -= OnError;
            _channel.Dispose();
        }
    }
}

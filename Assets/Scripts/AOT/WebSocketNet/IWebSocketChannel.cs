using System;
using System.Threading.Tasks;

namespace WebSocketNet
{
    /// <summary>
    /// WebSocket 通道抽象 —— 把"平台怎么收发字节"与"协议怎么编解码"分开。
    /// 实现有两个：DotNetWebSocketChannel（编辑器/原生，用 ClientWebSocket）
    /// 与 WebGLWebSocketChannel（仅 WebGL，走 jslib 到浏览器 API）。
    /// 分层的目的：适配器逻辑在 EditMode 单测时注入假通道即可，无需真 socket。
    /// </summary>
    public interface IWebSocketChannel : IDisposable
    {
        bool IsConnected { get; }

        Task<bool> ConnectAsync(string uri);
        Task CloseAsync(string reason);

        /// <summary>发送一帧（一条消息一帧，见 D-W9）</summary>
        Task SendAsync(byte[] data, int offset, int length);

        /// <summary>收到一帧。回调线程由实现决定：DotNet 是后台接收循环线程，WebGL 是 Unity 主线程（帧泵）</summary>
        event Action<byte[]>? MessageReceived;
        event Action<string>? Closed;
        event Action<Exception>? Error;
    }
}

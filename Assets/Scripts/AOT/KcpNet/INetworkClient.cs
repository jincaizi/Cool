using System;
using Cool.Protocol;

namespace KcpNet
{
    /// <summary>
    /// 网络客户端接口 —— Hotfix SyncLayer 依赖此抽象（适配器模式，项目既有规范）；
    /// 实现在 AOT 层（KcpNetClientAdapter），Hotfix 不直接触碰 KcpClient。
    /// </summary>
    public interface INetworkClient : IDisposable
    {
        bool IsConnected { get; }

        /// <summary>连接服务器（内部含 KCP 握手与接收循环启动）</summary>
        System.Threading.Tasks.Task<bool> ConnectAsync(string host, int port);

        System.Threading.Tasks.Task DisconnectAsync(string reason);

        void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable);

        /// <summary>消息接收事件（回调线程：当前实现为 Direct executor——网络驱动线程；
        /// 生产环境应换 UnityMainThreadExecutor 由宿主每帧泵送，见 KcpNetClientAdapter 注释）</summary>
        event Action<MessageId, IMessage>? MessageReceived;

        event Action<string>? Disconnected;

        event Action<Exception>? Error;
    }
}

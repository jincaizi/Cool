using System;
using System.Threading.Tasks;
using Cool.Protocol;

namespace WebSocketNet
{
    /// <summary>
    /// INetworkClient 的 WebSocket 实现 —— 占位骨架，Task 3 填充。
    /// 之所以在 Task 1 就建，是为了让 ClientNetworkHost 的平台分支在本任务即可编译。
    /// </summary>
    public sealed class WebSocketClientAdapter : INetworkClient
    {
        public bool IsConnected => false;

        public event Action<MessageId, IMessage>? MessageReceived;
        public event Action<string>? Disconnected;
        public event Action<Exception>? Error;

        public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(false);
        public Task DisconnectAsync(string reason) => Task.CompletedTask;
        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable) { }
        public void Dispose() { }
    }
}

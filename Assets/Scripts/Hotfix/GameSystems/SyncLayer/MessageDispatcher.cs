using System;
using System.Collections.Generic;
using Cool.Protocol;
using KcpNet;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 消息分发器 —— 按消息 ID 路由到订阅者（SyncLayer 入口唯一）。
    /// 线程模型：adapter 事件回调线程调用 Dispatch，订阅者处理须自行线程化或
    /// 由调用方保证主线程（P1 测试期 Direct 执行；生产接 UnityMainThreadExecutor）。
    /// </summary>
    public sealed class MessageDispatcher
    {
        private readonly Dictionary<MessageId, Action<IMessage>> _handlers = new Dictionary<MessageId, Action<IMessage>>();

        public void Subscribe(MessageId messageId, Action<IMessage> handler)
        {
            if (_handlers.TryGetValue(messageId, out var existing))
                _handlers[messageId] = existing + handler;
            else
                _handlers[messageId] = handler;
        }

        public void Unsubscribe(MessageId messageId, Action<IMessage> handler)
        {
            if (_handlers.TryGetValue(messageId, out var existing))
            {
                _handlers[messageId] = existing - handler;
                if (_handlers[messageId] == null) _handlers.Remove(messageId);
            }
        }

        public void Dispatch(MessageId messageId, IMessage message)
        {
            if (_handlers.TryGetValue(messageId, out var handler))
                handler(message);
        }
    }
}

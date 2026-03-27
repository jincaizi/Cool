using System;
using System.Collections.Generic;
using System.Threading;

namespace AOT.Core.GameEventDispatcher
{
    /// <summary>
    /// 全局事件分发器，支持注册/注销/派发事件
    /// </summary>
    public sealed class GameEventDispatcher
    {
        private static readonly Lazy<GameEventDispatcher> _instance = new Lazy<GameEventDispatcher>(() => new GameEventDispatcher(), LazyThreadSafetyMode.PublicationOnly);
        public static GameEventDispatcher Instance => _instance.Value;

        private readonly Dictionary<string, List<EventHandler>> _handlers = new Dictionary<string, List<EventHandler>>();
        private readonly object _lock = new object();
        private bool _disposed;

        private GameEventDispatcher() { }

        /// <summary>
        /// 注册事件处理器
        /// </summary>
        /// <param name="eventKey">事件键</param>
        /// <param name="handler">处理委托</param>
        public void Register(string eventKey, Action<object> handler)
        {
            if (string.IsNullOrEmpty(eventKey)) return;
            if (handler == null) return;

            lock (_lock)
            {
                if (!_handlers.TryGetValue(eventKey, out var handlers))
                {
                    handlers = new List<EventHandler>();
                    _handlers[eventKey] = handlers;
                }

                handlers.Add(new EventHandler { Callback = handler });
            }
        }

        /// <summary>
        /// 注销事件处理器
        /// </summary>
        /// <param name="eventKey">事件键</param>
        /// <param name="handler">处理委托</param>
        public void Unregister(string eventKey, Action<object> handler)
        {
            if (string.IsNullOrEmpty(eventKey)) return;
            if (handler == null) return;

            lock (_lock)
            {
                if (_handlers.TryGetValue(eventKey, out var handlers))
                {
                    handlers.RemoveAll(h => h.Callback == handler);
                }
            }
        }

        /// <summary>
        /// 派发事件
        /// </summary>
        /// <param name="eventKey">事件键</param>
        /// <param name="data">事件数据</param>
        public void Dispatch(string eventKey, object data = null)
        {
            if (string.IsNullOrEmpty(eventKey)) return;

            List<EventHandler>? handlersCopy = null;
            lock (_lock)
            {
                if (_handlers.TryGetValue(eventKey, out var handlers))
                {
                    handlersCopy = new List<EventHandler>(handlers);
                }
            }

            if (handlersCopy != null)
            {
                foreach (var handler in handlersCopy)
                {
                    try
                    {
                        handler.Callback?.Invoke(data);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"[GameEventDispatcher] Event {eventKey} handler error: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 清空指定事件的所有处理器
        /// </summary>
        /// <param name="eventKey">事件键</param>
        public void Clear(string eventKey)
        {
            lock (_lock)
            {
                if (_handlers.ContainsKey(eventKey))
                {
                    _handlers[eventKey].Clear();
                }
            }
        }

        /// <summary>
        /// 清空所有事件处理器
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                _handlers.Clear();
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearAll();
        }

        private sealed class EventHandler
        {
            public Action<object>? Callback;
        }
    }
}

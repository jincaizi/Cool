using System;
using System.Collections.Generic;

namespace Hotfix.GameSystems.UI.Framework.Message
{
    /// <summary>
    /// Independent UI message system.
    /// Separate from KCP networking messages.
    /// Used for internal UI communication.
    /// </summary>
    public struct UIMessageData
    {
        public string Type { get; set; }
        public object Body { get; set; }
        public long Timestamp { get; set; }

        public UIMessageData(string type, object body = null)
        {
            Type = type;
            Body = body;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    /// <summary>
    /// Message subscription entry for tracking.
    /// </summary>
    public class MessageCallback
    {
        public string Type { get; set; }
        public Action<object> Callback { get; set; }
    }

    /// <summary>
    /// Static message broker for pub/sub pattern.
    /// </summary>
    public static class UIMessage
    {
        private static readonly Dictionary<string, List<Action<object>>> _subscriptions = new();

        /// <summary>
        /// Subscribe to a message type.
        /// </summary>
        public static void Subscribe(string messageType, Action<object> callback)
        {
            if (callback == null) return;

            if (!_subscriptions.TryGetValue(messageType, out var handlers))
            {
                handlers = new List<Action<object>>();
                _subscriptions[messageType] = handlers;
            }

            // Avoid duplicate
            if (!handlers.Contains(callback))
            {
                handlers.Add(callback);
            }
        }

        /// <summary>
        /// Unsubscribe from a message type.
        /// </summary>
        public static void Unsubscribe(string messageType, Action<object> callback)
        {
            if (callback == null) return;

            if (_subscriptions.TryGetValue(messageType, out var handlers))
            {
                handlers.Remove(callback);
            }
        }

        /// <summary>
        /// Unsubscribe all handlers (call on scene change).
        /// </summary>
        public static void UnsubscribeAll()
        {
            _subscriptions.Clear();
        }

        /// <summary>
        /// Send a message to all subscribers.
        /// </summary>
        public static void Send(string messageType, object body = null)
        {
            var msg = new UIMessageData(messageType, body);

            if (_subscriptions.TryGetValue(messageType, out var handlers))
            {
                // Copy list to avoid modification during iteration
                var handlersCopy = new List<Action<object>>(handlers);
                foreach (var handler in handlersCopy)
                {
                    try
                    {
                        handler?.Invoke(body);
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError($"UIMessage handler error ({messageType}): {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Send a typed message (convenience overload).
        /// </summary>
        public static void Send<T>(string messageType, T body) where T : class
        {
            Send(messageType, (object)body);
        }
    }
}

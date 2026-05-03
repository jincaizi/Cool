using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 事件总线 - 轻量级事件发布订阅系统
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> _subscribers = new();
        private static bool _isPaused;

        /// <summary>
        /// 订阅事件（泛型版本，推荐使用）
        /// </summary>
        public static void Subscribe<T>(Action<T> callback) where T : IEvent
        {
            var type = typeof(T);
            if (!_subscribers.ContainsKey(type))
            {
                _subscribers[type] = new List<Delegate>();
            }
            _subscribers[type].Add(callback);

#if UNITY_3C_DEBUG
            Debug.Log($"[EventBus] Subscribed to {type.Name}");
#endif
        }

        /// <summary>
        /// 订阅事件（Type 版本，用于反射场景）
        /// </summary>
        public static void Subscribe(Type eventType, Action<IEvent> callback)
        {
            if (!_subscribers.ContainsKey(eventType))
            {
                _subscribers[eventType] = new List<Delegate>();
            }
            _subscribers[eventType].Add(callback);
        }

        /// <summary>
        /// 取消订阅（泛型版本）
        /// </summary>
        public static void Unsubscribe<T>(Action<T> callback) where T : IEvent
        {
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
            {
                list.Remove(callback);
#if UNITY_3C_DEBUG
                Debug.Log($"[EventBus] Unsubscribed from {type.Name}");
#endif
            }
        }

        /// <summary>
        /// 取消订阅（Type 版本）
        /// </summary>
        public static void Unsubscribe(Type eventType, Action<IEvent> callback)
        {
            if (_subscribers.TryGetValue(eventType, out var list))
            {
                list.Remove(callback);
            }
        }

        /// <summary>
        /// 发布事件（泛型版本，推荐使用）
        /// </summary>
        public static void Emit<T>(T evt) where T : IEvent
        {
            if (_isPaused) return;

            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
            {
                // 复制列表以防止在回调中修改列表导致的问题
                var callbacks = list.ToArray();
                foreach (var callback in callbacks)
                {
                    try
                    {
                        ((Action<T>)callback)?.Invoke(evt);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[EventBus] Exception in event callback for {type.Name}: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// 发布事件（非泛型版本）
        /// </summary>
        public static void Emit(IEvent evt)
        {
            if (_isPaused) return;

            var type = evt.GetType();
            if (_subscribers.TryGetValue(type, out var list))
            {
                var callbacks = list.ToArray();
                foreach (var callback in callbacks)
                {
                    try
                    {
                        callback?.Invoke(evt);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[EventBus] Exception in event callback for {type.Name}: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// 清空所有订阅
        /// </summary>
        public static void Clear()
        {
            _subscribers.Clear();
            Debug.Log("[EventBus] Cleared all subscribers");
        }

        /// <summary>
        /// 暂停事件分发
        /// </summary>
        public static void Pause()
        {
            _isPaused = true;
            Debug.Log("[EventBus] Paused");
        }

        /// <summary>
        /// 恢复事件分发
        /// </summary>
        public static void Resume()
        {
            _isPaused = false;
            Debug.Log("[EventBus] Resumed");
        }

        /// <summary>
        /// 获取订阅数量（用于调试）
        /// </summary>
        public static int GetSubscriberCount(Type eventType)
        {
            return _subscribers.TryGetValue(eventType, out var list) ? list.Count : 0;
        }

        /// <summary>
        /// 获取所有已订阅的事件类型（用于调试）
        /// </summary>
        public static Type[] GetSubscribedEventTypes()
        {
            var types = new Type[_subscribers.Count];
            _subscribers.Keys.CopyTo(types, 0);
            return types;
        }
    }
}
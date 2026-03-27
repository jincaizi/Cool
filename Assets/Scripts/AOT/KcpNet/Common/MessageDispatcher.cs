using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// 消息优先级
    /// </summary>
    public enum MessagePriority
    {
        /// <summary>
        /// 高优先级（控制消息、心跳响应等）
        /// </summary>
        High = 0,

        /// <summary>
        /// 中优先级（游戏逻辑消息）
        /// </summary>
        Medium = 1,

        /// <summary>
        /// 低优先级（位置同步、广播等）
        /// </summary>
        Low = 2
    }

    /// <summary>
    /// 消息分发器
    /// </summary>
    public sealed class MessageDispatcher : IDisposable
    {
        private readonly ConcurrentDictionary<Type, List<MessageHandler>> _handlers = new ConcurrentDictionary<Type, List<MessageHandler>>();
        private readonly ConcurrentQueue<MessageDispatchItem>[] _priorityQueues;
        private readonly SemaphoreSlim[] _queueSemaphores;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task[] _workerTasks;
        private readonly ILogger _logger;
        private readonly KcpOptions _options;
        private bool _disposed;

        /// <summary>
        /// 初始化消息分发器
        /// </summary>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        public MessageDispatcher(KcpOptions options, ILogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? NullLogger.Instance;

            // 创建优先级队列
            var priorityCount = Enum.GetValues(typeof(MessagePriority)).Length;
            _priorityQueues = new ConcurrentQueue<MessageDispatchItem>[priorityCount];
            _queueSemaphores = new SemaphoreSlim[priorityCount];

            for (int i = 0; i < priorityCount; i++)
            {
                _priorityQueues[i] = new ConcurrentQueue<MessageDispatchItem>();
                _queueSemaphores[i] = new SemaphoreSlim(0);
            }

            // 创建工作线程
            _workerTasks = new Task[_options.WorkerThreadCount];
            for (int i = 0; i < _options.WorkerThreadCount; i++)
            {
                _workerTasks[i] = Task.Run(() => WorkerLoopAsync(_cts.Token));
            }

            _logger.LogInformation($"MessageDispatcher started with {_options.WorkerThreadCount} worker threads");
        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="handler">处理器委托</param>
        /// <param name="priority">消息优先级</param>
        public void RegisterHandler<T>(Action<KcpSession, T, CancellationToken> handler, MessagePriority priority = MessagePriority.Medium) where T : IMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var messageType = typeof(T);
            var handlers = _handlers.GetOrAdd(messageType, _ => new List<MessageHandler>());
            var messageHandler = new MessageHandler
            {
                MessageType = messageType,
                Handler = (session, message, token) =>
                {
                    try
                    {
                        handler(session, (T)message, token);
                        return Task.CompletedTask;
                    }
                    catch (Exception ex)
                    {
                        return Task.FromException(ex);
                    }
                },
                Priority = priority
            };

            lock (handlers)
            {
                handlers.Add(messageHandler);
            }

            _logger.LogDebug($"Registered handler for {messageType.Name} with priority {priority}");
        }

        /// <summary>
        /// 注册异步消息处理器
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="handler">异步处理器委托</param>
        /// <param name="priority">消息优先级</param>
        public void RegisterHandler<T>(Func<KcpSession, T, CancellationToken, Task> handler, MessagePriority priority = MessagePriority.Medium) where T : IMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var messageType = typeof(T);
            var handlers = _handlers.GetOrAdd(messageType, _ => new List<MessageHandler>());
            var messageHandler = new MessageHandler
            {
                MessageType = messageType,
                Handler = async (session, message, token) => await handler(session, (T)message, token).ConfigureAwait(false),
                Priority = priority
            };

            lock (handlers)
            {
                handlers.Add(messageHandler);
            }

            _logger.LogDebug($"Registered async handler for {messageType.Name} with priority {priority}");
        }

        /// <summary>
        /// 分发消息
        /// </summary>
        /// <param name="session">会话</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="message">消息对象</param>
        /// <param name="priority">消息优先级</param>
        /// <returns>是否成功分发</returns>
        public bool Dispatch(KcpSession session, MessageId messageId, IMessage message, MessagePriority priority = MessagePriority.Medium)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (message == null) throw new ArgumentNullException(nameof(message));

            var messageType = message.GetType();
            if (!_handlers.ContainsKey(messageType))
            {
                _logger.LogWarning($"No handler registered for message type: {messageType.Name}");
                return false;
            }

            var item = new MessageDispatchItem
            {
                Session = session,
                MessageId = messageId,
                Message = message,
                Priority = priority,
                Timestamp = DateTime.UtcNow
            };

            var queueIndex = (int)priority;
            _priorityQueues[queueIndex].Enqueue(item);
            _queueSemaphores[queueIndex].Release();

            return true;
        }

        /// <summary>
        /// 异步分发消息
        /// </summary>
        /// <param name="session">会话</param>
        /// <param name="rawData">原始数据</param>
        /// <returns>分发任务</returns>
        public async Task DispatchAsync(KcpSession session, byte[] rawData)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (rawData == null) throw new ArgumentNullException(nameof(rawData));

            try
            {
                var (messageId, message) = MessageCodec.Decode(rawData);
                var messageType = message.GetType();

                // 确定消息优先级
                var priority = DeterminePriority(messageType, messageId);

                // 分发消息
                if (!Dispatch(session, messageId, message, priority))
                {
                    _logger.LogWarning($"Failed to dispatch message: {messageType.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to decode or dispatch message");
            }
        }

        /// <summary>
        /// 获取已注册的消息类型
        /// </summary>
        public IEnumerable<Type> GetRegisteredMessageTypes()
        {
            return _handlers.Keys;
        }

        /// <summary>
        /// 获取指定消息类型的处理器数量
        /// </summary>
        public int GetHandlerCount(Type messageType)
        {
            if (_handlers.TryGetValue(messageType, out var handlers))
            {
                return handlers.Count;
            }
            return 0;
        }

        /// <summary>
        /// 清除所有处理器
        /// </summary>
        public void ClearHandlers()
        {
            _handlers.Clear();
            _logger.LogInformation("Cleared all message handlers");
        }

        /// <summary>
        /// 清除指定消息类型的处理器
        /// </summary>
        public void ClearHandlers(Type messageType)
        {
            if (_handlers.TryRemove(messageType, out _))
            {
                _logger.LogDebug($"Cleared handlers for {messageType.Name}");
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cts.Cancel();

            try
            {
                Task.WaitAll(_workerTasks, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while waiting for worker tasks to complete");
            }

            _cts.Dispose();
            foreach (var semaphore in _queueSemaphores)
            {
                semaphore.Dispose();
            }

            _logger.LogInformation("MessageDispatcher disposed");
        }

        /// <summary>
        /// 工作线程循环
        /// </summary>
        private async Task WorkerLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug($"Worker thread started: {Environment.CurrentManagedThreadId}");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 按优先级顺序检查队列（高优先级优先）
                    for (int i = 0; i < _priorityQueues.Length; i++)
                    {
                        if (_queueSemaphores[i].Wait(0))
                        {
                            if (_priorityQueues[i].TryDequeue(out var item))
                            {
                                await ProcessMessageAsync(item, cancellationToken).ConfigureAwait(false);
                            }
                            break; // 处理一个消息后继续循环
                        }
                    }

                    // 如果没有消息，短暂休眠
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in worker thread {Environment.CurrentManagedThreadId}");
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }

            _logger.LogDebug($"Worker thread stopped: {Environment.CurrentManagedThreadId}");
        }

        /// <summary>
        /// 处理消息
        /// </summary>
        private async Task ProcessMessageAsync(MessageDispatchItem item, CancellationToken cancellationToken)
        {
            var messageType = item.Message.GetType();
            if (!_handlers.TryGetValue(messageType, out var handlers))
            {
                _logger.LogWarning($"No handlers found for message type: {messageType.Name}");
                return;
            }

            // 处理消息延迟
            var delay = DateTime.UtcNow - item.Timestamp;
            if (delay.TotalMilliseconds > 1000)
            {
                _logger.LogWarning($"Message processing delayed: {messageType.Name} delayed {delay.TotalMilliseconds}ms");
            }

            // 执行所有处理器
            var tasks = new List<Task>();
            foreach (var handler in handlers.ToList()) // 复制列表以避免并发修改
            {
                try
                {
                    var task = handler.Handler(item.Session, item.Message, cancellationToken);
                    if (task != null)
                    {
                        tasks.Add(task);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in message handler for {messageType.Name}");
                }
            }

            // 等待所有处理器完成
            if (tasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in message handlers for {messageType.Name}");
                }
            }

            // 更新会话活动时间
            item.Session.UpdateLastActivity();
        }

        /// <summary>
        /// 确定消息优先级
        /// </summary>
        private MessagePriority DeterminePriority(Type messageType, MessageId messageId)
        {
            // 高优先级消息
            if (messageId == MessageId.Heartbeat ||
                messageId == MessageId.Kick ||
                messageId == MessageId.ShutdownNotification)
            {
                return MessagePriority.High;
            }

            // 低优先级消息
            if (messageId == MessageId.PositionSyncRequest ||
                messageId == MessageId.PositionSyncResponse ||
                messageId == MessageId.BroadcastMessage)
            {
                return MessagePriority.Low;
            }

            // 默认中优先级
            return MessagePriority.Medium;
        }

        /// <summary>
        /// 消息处理器封装
        /// </summary>
        private sealed class MessageHandler
        {
            public Type MessageType { get; set; } = null!;
            public Func<KcpSession, object, CancellationToken, Task> Handler { get; set; } = null!;
            public MessagePriority Priority { get; set; }
        }

        /// <summary>
        /// 消息分发项
        /// </summary>
        private sealed class MessageDispatchItem
        {
            public KcpSession Session { get; set; } = null!;
            public MessageId MessageId { get; set; }
            public IMessage Message { get; set; } = null!;
            public MessagePriority Priority { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}
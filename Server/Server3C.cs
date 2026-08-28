using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    public sealed class Server3C : IDisposable
    {
        private readonly ILogger _logger;
        private readonly RoomManager _roomManager;
        private readonly LoginHandler _loginHandler;
        private readonly PositionSyncHandler _positionSyncHandler;
        private readonly EchoHandler _echoHandler;
        private readonly ClockSyncHandler _clockSyncHandler;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private Socket? _udpServer;
        private Task? _receiveTask;
        private Task? _cleanupTask;
        private bool _disposed;

        private readonly SessionManager _sessionManager;
        private readonly ConcurrentDictionary<long, long> _sessionIdToPlayerId = new ConcurrentDictionary<long, long>();
        private readonly ConcurrentDictionary<long, long> _playerIdToSessionId = new ConcurrentDictionary<long, long>();
        private readonly ConcurrentDictionary<long, KcpServerSession> _playerIdToSession = new ConcurrentDictionary<long, KcpServerSession>();

        // R2：唯一消费者线程独占世界状态；IO 线程只 Enqueue，不触碰状态
        private readonly GameLoop<(EndPoint endpoint, byte[] data)> _gameLoop;

        // T4：世界快照广播链（主循环独占访问）
        private readonly EntityStore _entityStore = new EntityStore();
        private readonly SnapshotAssembler _snapshotAssembler = new SnapshotAssembler();
        private readonly IPerceptionFilter _perceptionFilter = new AllVisiblePerceptionFilter();
        private const uint BeaconIntervalTicks = 4; // 3.75Hz beacon（每 4 tick 一次，15Hz tick）

        // 超时配置
        private readonly TimeSpan _sessionTimeout = TimeSpan.FromSeconds(300);
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(30);

        public RoomManager RoomManager => _roomManager;
        public int PlayerCount => _roomManager.TotalPlayerCount;
        public int ActiveSessionCount => _sessionManager.SessionCount;

        public Server3C(ILogger logger)
        {
            _logger = logger;
            _sessionManager = new SessionManager(logger);
            _roomManager = new RoomManager(new RoomManagerConfig());
            _loginHandler = new LoginHandler(this, _roomManager, _logger);
            _positionSyncHandler = new PositionSyncHandler(this, _roomManager, _logger);
            _echoHandler = new EchoHandler(this, _logger);
            _clockSyncHandler = new ClockSyncHandler(this, _logger);

            // R2：唯一消费者线程独占世界状态；handler 同步链内完成解码/分发/发送
            _gameLoop = new GameLoop<(EndPoint, byte[])>(
                TimeSpan.FromMilliseconds(66),   // ~15Hz
                packet => HandlePacket(packet.Item1, packet.Item2),
                logger,
                tick => BroadcastWorldSnapshot((uint)tick));
        }

        public Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Server3C));

            _udpServer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpServer.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpServer.Bind(new IPEndPoint(IPAddress.Any, port));

            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
            _gameLoop.Start();
            _cleanupTask = Task.Run(() => CleanupLoop(_cts.Token), _cts.Token);

            _logger.LogInformation($"Server3C started on port {port} with KCP protocol");
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_udpServer == null) return;

            _logger.LogInformation("Stopping Server3C...");

            _cts.Cancel();

            // 关闭所有会话
            foreach (var s in _sessionManager.AllSessions)
            {
                await s.CloseAsync("ServerShutdown");
                s.Dispose();
            }

            if (_receiveTask != null)
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));

            await _gameLoop.Stop();

            if (_cleanupTask != null)
                await _cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));

            _udpServer.Close();
            _udpServer = null;

            _logger.LogInformation("Server3C stopped");
        }

        public void BindPlayerToSession(long playerId, KcpServerSession session)
        {
            _playerIdToSessionId[playerId] = session.SessionId;
            _sessionIdToPlayerId[session.SessionId] = playerId;
            _playerIdToSession[playerId] = session;
        }

        public void UnbindPlayer(long playerId)
        {
            if (_playerIdToSessionId.TryRemove(playerId, out var sessionId))
            {
                _sessionIdToPlayerId.TryRemove(sessionId, out _);
            }
            _playerIdToSession.TryRemove(playerId, out _);
        }

        public void RegisterEntity(long playerId, string playerName)
        {
            _entityStore.AddPlayer(playerId, playerName);
        }

        public void UnregisterEntity(long playerId)
        {
            _entityStore.RemovePlayer(playerId);
        }

        public long? GetPlayerId(KcpServerSession session)
        {
            return _sessionIdToPlayerId.TryGetValue(session.SessionId, out var playerId) ? playerId : null;
        }

        public KcpServerSession? GetSession(long playerId)
        {
            return _playerIdToSession.TryGetValue(playerId, out var session) ? session : null;
        }

        public Task SendToSessionAsync(KcpServerSession session, MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
        {
            if (_udpServer == null || !session.IsConnected)
                return Task.CompletedTask;

            try
            {
                session.Send(messageId, message, flags);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send message to session {session.SessionId}");
            }
            return Task.CompletedTask;
        }

        /// <summary>每 tick 广播世界快照（脏实体 + beacon；广播给各玩家的可见集）</summary>
        private void BroadcastWorldSnapshot(uint tick)
        {
            var dirty = _snapshotAssembler.BuildDirtySnapshot(_entityStore, tick);
            var beacons = _snapshotAssembler.BuildBeaconSnapshot(_entityStore, tick, BeaconIntervalTicks);

            var allEntities = new System.Collections.Generic.List<Cool.Protocol.EntitySnapshot>(dirty.Count + beacons.Count);
            allEntities.AddRange(dirty);
            allEntities.AddRange(beacons);
            if (allEntities.Count == 0) return; // 全静世界：不发快照（带宽省）

            foreach (var observer in _entityStore.All)
            {
                if (!_playerIdToSession.TryGetValue(observer.PlayerId, out var session)) continue;

                // 按感知过滤契约组装可见集（P1 全量占位 = 除自身外全体；P3 AoiGrid 替换）
                var visibleIds = _perceptionFilter.GetVisiblePlayerIds(observer.PlayerId, _entityStore.All);
                var visible = allEntities.Where(e => System.Array.IndexOf(visibleIds, e.PlayerId) >= 0).ToList();
                if (visible.Count == 0) continue;

                var observerSnapshot = new WorldSnapshot
                {
                    Tick = tick,
                    ServerTimestamp = System.DateTime.UtcNow.Ticks,
                    Entities = visible
                };
                session.Send(MessageId.WorldSnapshot, observerSnapshot);
            }
        }

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Receive loop started");

            var buffer = new byte[65536];
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (!cancellationToken.IsCancellationRequested && _udpServer != null)
            {
                try
                {
                    if (_udpServer.Available > 0)
                    {
                        var bytesRead = _udpServer.ReceiveFrom(buffer, ref remoteEndPoint);
                        if (bytesRead > 0)
                        {
                            var data = new byte[bytesRead];
                            Array.Copy(buffer, 0, data, 0, bytesRead);
                            _gameLoop.Enqueue((remoteEndPoint, data));
                        }
                    }
                    else
                    {
                        await Task.Delay(1, cancellationToken);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset)
                {
                    // UDP 端口不可达（ICMP）——静默忽略（P0 终审 SHOULD 项）
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in receive loop");
                }
            }

            _logger.LogInformation("Receive loop stopped");
        }

        private void HandlePacket(EndPoint remoteEndPoint, byte[] data)
        {
            try
            {
                var session = _sessionManager.GetOrCreateSession(remoteEndPoint, data);
                if (session == null) return;

                if (session.SendSocket == null)
                    session.SetSendSocket(_udpServer!);

                // 幂等接线：事件订阅先于任何包处理（消除发布-订阅窗口竞态）
                session.EnsureWired(() =>
                {
                    session.DataReceived += OnSessionDataReceived;
                    session.Disconnected += OnSessionDisconnected;
                    session.Error += OnSessionError;
                });

                session.HandleUdpPacket(data, data.Length);
                session.UpdateActivity();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling packet");
            }
        }

        private void OnSessionDataReceived(object? sender, byte[] data)
        {
            if (sender is KcpServerSession session)
            {
                ProcessPacket(session, data);
            }
        }

        private async Task ProcessPacket(KcpServerSession session, byte[] data)
        {
            try
            {
                var (messageId, message) = MessageCodec.Decode(data);
                session.UpdateActivity();

                switch (message)
                {
                    case LoginRequest login:
                        await _loginHandler.HandleLoginAsync(session, login);
                        break;

                    case PositionSyncRequest posSync:
                        await _positionSyncHandler.HandlePositionSyncAsync(session, posSync);
                        break;

                    case EchoRequest echoRequest:
                        await _echoHandler.HandleEchoAsync(session, echoRequest);
                        break;

                    case ClockSyncRequest clockSync:
                        await _clockSyncHandler.HandleClockSyncAsync(session, clockSync);
                        break;

                    case Heartbeat heartbeat:
                        _logger.LogDebug($"Heartbeat from session {session.SessionId}: seq={heartbeat.Sequence}");
                        // 回应心跳
                        await SendToSessionAsync(session, MessageId.Heartbeat, heartbeat);
                        break;

                    default:
                        _logger.LogWarning($"Unknown message type: {messageId} from session {session.SessionId}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing packet from session {session.SessionId}");
            }
        }

        private void OnSessionDisconnected(object? sender, EventArgs e)
        {
            if (sender is KcpServerSession session)
            {
                _logger.LogInformation($"Session {session.SessionId} disconnected");
                // R2：世界状态变更一律经主循环动作队列
                _gameLoop.EnqueueAction(() => { _ = _loginHandler.HandleDisconnectAsync(session); });
                _gameLoop.EnqueueAction(() => _sessionManager.RemoveByEndpoint(session.RemoteEndPoint));
            }
        }

        private void OnSessionError(object? sender, Exception ex)
        {
            if (sender is KcpServerSession session)
            {
                _logger.LogError(ex, $"Session {session.SessionId} error");
            }
        }

        private async Task CleanupLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                    // 清理超时会话
                    var timeoutSessions = new System.Collections.Generic.List<KcpServerSession>();
                    foreach (var s in _sessionManager.AllSessions)
                    {
                        if (s.IsTimedOut(_sessionTimeout))
                        {
                            timeoutSessions.Add(s);
                        }
                    }

                    foreach (var s in timeoutSessions)
                    {
                        _logger.LogInformation($"Session {s.SessionId} timed out");
                        // 断开清理统一走事件路径（CloseAsync 同步触发 Disconnected → OnSessionDisconnected 入队），避免双执行
                        await s.CloseAsync("Timeout");
                        s.Dispose();
                    }

                    // 清理空房间（R2：经主循环）
                    _gameLoop.EnqueueAction(() => _roomManager.CleanupEmptyRooms());

                    if (PlayerCount > 0)
                    {
                        _logger.LogInformation($"Server stats - Players: {PlayerCount}, Sessions: {ActiveSessionCount}, Rooms: {_roomManager.RoomCount}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in cleanup loop");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cts.Cancel();

            // 停掉主循环（不走 StopAsync 直接 Dispose 时防止循环线程泄漏）
            _gameLoop.Dispose();

            // SessionManager 内部 Dispose 各会话（事件退订随会话引用一并释放）
            _sessionManager.Dispose();

            _cts.Dispose();
        }
    }
}
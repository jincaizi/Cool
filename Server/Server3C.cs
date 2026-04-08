using System;
using System.Collections.Concurrent;
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
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private Socket? _udpServer;
        private Task? _receiveTask;
        private Task? _cleanupTask;
        private long _nextSessionId = 1;
        private bool _disposed;

        private readonly ConcurrentDictionary<long, KcpServerSession> _sessions = new ConcurrentDictionary<long, KcpServerSession>();
        private readonly ConcurrentDictionary<long, long> _sessionIdToPlayerId = new ConcurrentDictionary<long, long>();
        private readonly ConcurrentDictionary<long, long> _playerIdToSessionId = new ConcurrentDictionary<long, long>();
        private readonly ConcurrentDictionary<long, KcpServerSession> _playerIdToSession = new ConcurrentDictionary<long, KcpServerSession>();

        // 超时配置
        private readonly TimeSpan _sessionTimeout = TimeSpan.FromSeconds(300);
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(30);

        public RoomManager RoomManager => _roomManager;
        public int PlayerCount => _roomManager.TotalPlayerCount;
        public int ActiveSessionCount => _sessions.Count;

        public Server3C(ILogger logger)
        {
            _logger = logger;
            _roomManager = new RoomManager(new RoomManagerConfig());
            _loginHandler = new LoginHandler(this, _roomManager, _logger);
            _positionSyncHandler = new PositionSyncHandler(this, _roomManager, _logger);
        }

        public Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Server3C));

            _udpServer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpServer.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpServer.Bind(new IPEndPoint(IPAddress.Any, port));

            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
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
            foreach (var s in _sessions.Values)
            {
                await s.CloseAsync("ServerShutdown");
                s.Dispose();
            }
            _sessions.Clear();

            if (_receiveTask != null)
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
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
                            _ = Task.Run(() => HandlePacket(remoteEndPoint, data), cancellationToken);
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in receive loop");
                }
            }

            _logger.LogInformation("Receive loop stopped");
        }

        private async Task HandlePacket(EndPoint remoteEndPoint, byte[] data)
        {
            try
            {
                // 查找或创建会话
                var session = FindOrCreateSession(remoteEndPoint);
                if (session == null) return;

                // KCP 数据包
                session.HandleUdpPacket(data, data.Length);
                session.UpdateActivity();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling packet");
            }
        }

        private KcpServerSession? FindOrCreateSession(EndPoint remoteEndPoint)
        {
            // 查找现有会话
            foreach (var s in _sessions.Values)
            {
                if (s.RemoteEndPoint.Equals(remoteEndPoint))
                {
                    return s;
                }
            }

            // 创建新会话
            long sessionId = Interlocked.Increment(ref _nextSessionId);
            var session = new KcpServerSession(sessionId, remoteEndPoint, _udpServer!, _logger);

            // 订阅会话事件
            session.DataReceived += OnSessionDataReceived;
            session.Disconnected += OnSessionDisconnected;
            session.Error += OnSessionError;

            _sessions[sessionId] = session;
            _logger.LogInformation($"New KCP session: {sessionId} from {remoteEndPoint}");

            return session;
        }

        private void OnSessionDataReceived(object? sender, byte[] data)
        {
            if (sender is KcpServerSession session)
            {
                _ = Task.Run(() => ProcessPacket(session, data));
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

        private async void OnSessionDisconnected(object? sender, EventArgs e)
        {
            if (sender is KcpServerSession session)
            {
                _logger.LogInformation($"Session {session.SessionId} disconnected");

                // 通知处理器
                await _loginHandler.HandleDisconnectAsync(session);

                // 移除会话
                _sessions.TryRemove(session.SessionId, out _);
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
                    foreach (var s in _sessions.Values)
                    {
                        if (s.IsTimedOut(_sessionTimeout))
                        {
                            timeoutSessions.Add(s);
                        }
                    }

                    foreach (var s in timeoutSessions)
                    {
                        _logger.LogInformation($"Session {s.SessionId} timed out");
                        _sessions.TryRemove(s.SessionId, out _);
                        await s.CloseAsync("Timeout");
                        s.Dispose();

                        // 处理断开
                        await _loginHandler.HandleDisconnectAsync(s);
                    }

                    // 清理空房间
                    _roomManager.CleanupEmptyRooms();

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

            foreach (var s in _sessions.Values)
            {
                s.DataReceived -= OnSessionDataReceived;
                s.Disconnected -= OnSessionDisconnected;
                s.Error -= OnSessionError;
                s.Dispose();
            }
            _sessions.Clear();

            _cts.Dispose();
        }
    }
}
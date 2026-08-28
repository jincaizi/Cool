using System;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    public class LoginHandler
    {
        private readonly Server3C _server;
        private readonly RoomManager _roomManager;
        private readonly ILogger _logger;
        private long _nextPlayerId = 1;

        public LoginHandler(Server3C server, RoomManager roomManager, ILogger logger)
        {
            _server = server;
            _roomManager = roomManager;
            _logger = logger;
        }

        public async Task HandleLoginAsync(KcpServerSession session, LoginRequest request)
        {
            if (request.ProtocolVersion != ProtocolConstants.ProtocolVersion)
            {
                var reject = new LoginResponse
                {
                    Success = false,
                    Message = $"Protocol version mismatch: client={request.ProtocolVersion}, server={ProtocolConstants.ProtocolVersion}",
                    ServerTimestamp = DateTime.UtcNow.Ticks
                };
                await _server.SendToSessionAsync(session, MessageId.LoginResponse, reject);
                await _server.SendToSessionAsync(session, MessageId.Kick, new Kick
                {
                    Reason = "ProtocolVersionMismatch",
                    KickType = 0,
                    AllowReconnectAfter = 0
                });
                // KCP Send 仅入队 snd_queue，Flush 在会话 update loop（10ms/周期）执行——
                // 等待一个 flush 窗口（50ms = 5 个周期）再关闭，确保 reject+kick 真正上线
                //（否则 CloseAsync 取消 CTS 后队列被丢弃，客户端静默挂起）
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                // 版本拒绝后关闭会话：CloseAsync 触发 Disconnected → 清理路径（会话被正常回收而非挂到超时）
                await session.CloseAsync("ProtocolVersionMismatch");
                _logger.LogWarning($"Rejected login from {session.RemoteEndPoint}: protocol version {request.ProtocolVersion}");
                return;
            }

            _logger.LogInformation($"Login attempt: {request.Username} from {session.RemoteEndPoint}");

            long playerId = Interlocked.Increment(ref _nextPlayerId);

            var playerState = new PlayerState(playerId)
            {
                PlayerName = request.Username,
                IsLoggedIn = true,
                Position = new Vector3(0f, 0f, 0f)
            };

            _server.BindPlayerToSession(playerId, session);
            _server.RegisterEntity(playerId, request.Username);

            string zoneId = string.IsNullOrEmpty(request.ZoneId) ? "Global" : request.ZoneId;
            _roomManager.TryAddPlayerToRoom(playerId, session, playerState, zoneId);

            // 发送登录响应
            var loginResponse = new LoginResponse
            {
                Success = true,
                Message = "Login successful",
                PlayerId = playerId,
                ZoneId = zoneId,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };
            await _server.SendToSessionAsync(session, MessageId.LoginResponse, loginResponse);

            // 发送房间同步
            var room = _roomManager.GetPlayerRoom(playerId);
            if (room != null)
            {
                var roomSync = new RoomSync
                {
                    ZoneId = room.RoomId,
                    Players = room.GetAllRemotePlayerSync(),
                    ServerTimestamp = DateTime.UtcNow.Ticks
                };
                await _server.SendToSessionAsync(session, MessageId.RoomSync, roomSync);
            }

            // 通知其他玩家
            if (room != null)
            {
                var enterRoom = new PlayerEnterRoom
                {
                    PlayerId = playerId,
                    PlayerName = request.Username,
                    ZoneId = zoneId,
                    X = playerState.Position.X,
                    Y = playerState.Position.Y,
                    Z = playerState.Position.Z,
                    Rotation = playerState.Rotation,
                    ServerTimestamp = DateTime.UtcNow.Ticks
                };

                foreach (var otherSession in room.GetOtherPlayerSessions(playerId))
                {
                    await _server.SendToSessionAsync(otherSession, MessageId.PlayerEnterRoom, enterRoom);
                }
            }

            _logger.LogInformation($"Player {playerId} ({request.Username}) logged in to zone {zoneId}");
        }

        public async Task HandleDisconnectAsync(KcpServerSession session)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue)
                return;

            var room = _roomManager.GetPlayerRoom(playerId.Value);
            var playerState = room?.GetPlayerState(playerId.Value);
            string playerName = playerState?.PlayerName ?? "Unknown";

            _roomManager.TryRemovePlayer(playerId.Value);
            _server.UnbindPlayer(playerId.Value);
            _server.UnregisterEntity(playerId.Value);

            if (room != null)
            {
                var leaveRoom = new PlayerLeaveRoom
                {
                    PlayerId = playerId.Value,
                    Reason = "Disconnected",
                    ServerTimestamp = DateTime.UtcNow.Ticks
                };

                foreach (var otherSession in room.GetAllPlayerSessions())
                {
                    await _server.SendToSessionAsync(otherSession, MessageId.PlayerLeaveRoom, leaveRoom);
                }
            }

            _logger.LogInformation($"Player {playerId} ({playerName}) disconnected");
        }
    }
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cool.SyncCore;

namespace KcpServer
{
    public class LoginHandler
    {
        private readonly Server3C _server;
        private readonly RoomManager _roomManager;
        private readonly ILogger _logger;
        private long _nextPlayerId = 1;
        private long _nextWeaponInstanceId = 1;

        public LoginHandler(Server3C server, RoomManager roomManager, ILogger logger)
        {
            _server = server;
            _roomManager = roomManager;
            _logger = logger;
        }

        public async Task HandleLoginAsync(IServerSession session, LoginRequest request)
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
                _logger.LogWarning($"Rejected login from session {session.SessionId}: protocol version {request.ProtocolVersion}");
                return;
            }

            _logger.LogInformation($"Login attempt: {request.Username} from session {session.SessionId}");

            bool wantsElemental = (request.SupportedFeatures & ProtocolFeatures.ElementalSwordQiV1) != 0;
            if (wantsElemental && (!_server.ElementalSwordQiEnabled
                || !string.Equals(request.ElementalCatalogFingerprint,
                    _server.ElementalCatalogFingerprint, StringComparison.Ordinal)))
            {
                var reject = new LoginResponse
                {
                    Success = false,
                    Message = "elemental catalog fingerprint mismatch",
                    ServerTimestamp = DateTime.UtcNow.Ticks
                };
                await _server.SendToSessionAsync(session, MessageId.LoginResponse, reject);
                // 与协议版本拒绝同序：留一个 flush 窗口再关会话，避免未认证会话长期挂起。
                await Task.Delay(50);
                await session.CloseAsync("ElementalCatalogMismatch");
                _logger.LogWarning($"Rejected elemental login from session {session.SessionId}: catalog fingerprint mismatch");
                return;
            }

            long playerId = Interlocked.Increment(ref _nextPlayerId);

            var playerState = new PlayerState(playerId)
            {
                PlayerName = request.Username,
                IsLoggedIn = true,
                Position = new Vector3(0f, 0f, 0f)
            };

            _server.BindPlayerToSession(playerId, session);
            var storePlayer = _server.RegisterEntity(playerId, request.Username,
                (request.SupportedFeatures & ProtocolFeatures.AuthoritativeRespawn) != 0);

            string zoneId = string.IsNullOrEmpty(request.ZoneId) ? "Global" : request.ZoneId;
            storePlayer.ZoneId = zoneId;
            if (wantsElemental) GrantStartingWeapons(storePlayer);
            _roomManager.TryAddPlayerToRoom(playerId, session, playerState, zoneId);

            // 发送登录响应
            var loginResponse = new LoginResponse
            {
                Success = true,
                Message = "Login successful",
                PlayerId = playerId,
                ZoneId = zoneId,
                ServerTimestamp = DateTime.UtcNow.Ticks,
                ServerTickRateMs = 66 // 服务器 GameLoop 66ms（Server3C GameLoop ctor）——客户端据此换算插值窗口
            };
            if (wantsElemental)
            {
                loginResponse.EnabledFeatures = ProtocolFeatures.ElementalSwordQiV1;
                loginResponse.ElementalCatalogFingerprint = _server.ElementalCatalogFingerprint;
                loginResponse.WeaponInventory = EquipHandler.BuildBaseline(storePlayer, _server.ElementalCatalog);
            }
            await _server.SendToSessionAsync(session, MessageId.LoginResponse, loginResponse);

            // RoomSync 登录下发退役（P2 T3）：新玩家看已在线玩家 = EntityStore 注册标脏 →
            // 下一 tick WorldSnapshot 广播（Room 存储的 PlayerState 位置已成为陈旧数据源）

            var room = _roomManager.GetPlayerRoom(playerId);

            // 名字名册：把房间内已有玩家以 PlayerEnterRoom 发给新玩家（客户端据此显示远程名字；
            // 位置字段客户端忽略，以 WorldSnapshot 为准）。原有"通知其他人"保留。
            if (room != null)
            {
                foreach (var existing in BuildRoster(room.GetAllPlayerStates(), playerId, zoneId))
                    await _server.SendToSessionAsync(session, MessageId.PlayerEnterRoom, existing);
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

        /// <summary>按共享目录发放会话内武器实例并装备默认剑。重登会重新发放（新实例 ID）。</summary>
        public void GrantStartingWeapons(PlayerState player)
        {
            var catalog = _server.ElementalCatalog;
            long defaultInstanceId = 0;
            foreach (int templateId in catalog.StartingWeaponTemplateIds)
            {
                long instanceId = _nextWeaponInstanceId++;
                player.WeaponInstanceTemplates[instanceId] = templateId;
                if (templateId == catalog.DefaultWeaponTemplateId && defaultInstanceId == 0)
                    defaultInstanceId = instanceId;
            }

            player.EquippedWeaponInstanceId = defaultInstanceId;
            player.EquippedWeaponTemplateId = catalog.DefaultWeaponTemplateId;
            player.EquipmentRevision = 0;
        }

        /// <summary>登录名字名册：房间内已有玩家（排除新玩家本人）→ PlayerEnterRoom 列表。
        /// 位置/朝向不填（客户端忽略，服务端权威位置走 WorldSnapshot）。</summary>
        public static List<PlayerEnterRoom> BuildRoster(
            IReadOnlyDictionary<long, PlayerState> players, long excludePlayerId, string zoneId)
        {
            var list = new List<PlayerEnterRoom>();
            foreach (var pair in players)
            {
                if (pair.Key == excludePlayerId) continue;
                list.Add(new PlayerEnterRoom
                {
                    PlayerId = pair.Key,
                    PlayerName = pair.Value.PlayerName,
                    ZoneId = zoneId,
                    ServerTimestamp = DateTime.UtcNow.Ticks
                });
            }
            return list;
        }

        public async Task HandleDisconnectAsync(IServerSession session)
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
            _server.RemoveObserver(playerId.Value); // 断开清 observer AOI 视野/节拍状态

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

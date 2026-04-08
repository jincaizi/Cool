using System;
using System.Threading.Tasks;

namespace KcpServer
{
    public class PositionSyncHandler
    {
        private readonly Server3C _server;
        private readonly RoomManager _roomManager;
        private readonly ILogger _logger;

        private const float MAX_SPEED = 15f;
        private const float MAX_TELEPORT_DISTANCE = 50f;
        private const float MAX_JUMP_HEIGHT = 3f;

        public PositionSyncHandler(Server3C server, RoomManager roomManager, ILogger logger)
        {
            _server = server;
            _roomManager = roomManager;
            _logger = logger;
        }

        public async Task HandlePositionSyncAsync(KcpServerSession session, PositionSyncRequest request)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue)
            {
                _logger.LogWarning($"PositionSync from unknown session {session.SessionId}");
                return;
            }

            var room = _roomManager.GetPlayerRoom(playerId.Value);
            if (room == null)
            {
                _logger.LogWarning($"Player {playerId} not in any room");
                return;
            }

            var playerState = room.GetPlayerState(playerId.Value);
            if (playerState == null)
            {
                _logger.LogWarning($"Player {playerId} state not found");
                return;
            }

            Vector3 newPosition = new Vector3(request.X, request.Y, request.Z);
            float newRotation = request.Rotation;
            float speed = request.Speed;
            long timestamp = request.Timestamp;
            uint sequence = request.Sequence;

            bool needsCorrection = false;
            Vector3 authoritativePosition = newPosition;
            float authoritativeRotation = newRotation;

            if (speed > MAX_SPEED)
            {
                _logger.LogWarning($"Player {playerId} speed too high: {speed} > {MAX_SPEED}");
                speed = MAX_SPEED;
                needsCorrection = true;
            }

            float distance = Vector3.Distance(playerState.Position, newPosition);
            long timeDelta = timestamp - playerState.LastUpdateTimestamp;
            float timeDeltaSeconds = timeDelta / (float)TimeSpan.TicksPerSecond;

            if (timeDeltaSeconds > 0)
            {
                float maxPossibleDistance = MAX_SPEED * timeDeltaSeconds * 1.2f;
                if (distance > maxPossibleDistance && distance > MAX_TELEPORT_DISTANCE)
                {
                    _logger.LogWarning($"Player {playerId} possible teleport: {distance}m in {timeDeltaSeconds}s");
                    needsCorrection = true;
                    authoritativePosition = playerState.Position + Vector3.forward * MAX_SPEED * timeDeltaSeconds;
                }
            }

            float verticalDelta = newPosition.Y - playerState.Position.Y;
            if (verticalDelta > MAX_JUMP_HEIGHT)
            {
                needsCorrection = true;
            }

            playerState.UpdatePosition(authoritativePosition, authoritativeRotation, speed, timestamp, sequence);

            // 广播给其他玩家
            var remoteSync = new RemotePlayerSync
            {
                PlayerId = playerId.Value,
                X = playerState.Position.X,
                Y = playerState.Position.Y,
                Z = playerState.Position.Z,
                Rotation = playerState.Rotation,
                Speed = playerState.Speed,
                Timestamp = timestamp,
                Sequence = sequence
            };

            foreach (var otherSession in room.GetOtherPlayerSessions(playerId.Value))
            {
                await _server.SendToSessionAsync(otherSession, MessageId.RemotePlayerSync, remoteSync);
            }

            // 发送响应给客户端
            var response = new PositionSyncResponse
            {
                AcknowledgedSequence = sequence,
                ServerTimestamp = DateTime.UtcNow.Ticks,
                HasPositionCorrection = needsCorrection,
                AuthoritativeX = authoritativePosition.X,
                AuthoritativeY = authoritativePosition.Y,
                AuthoritativeZ = authoritativePosition.Z,
                AuthoritativeRotation = authoritativeRotation
            };

            await _server.SendToSessionAsync(session, MessageId.PositionSyncResponse, response);
        }
    }
}
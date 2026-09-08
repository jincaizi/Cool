using System;
using System.Threading.Tasks;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>
    /// 位置同步处理器 —— 校验（共享 MovementValidator）+ EntityStore 应用（P2 接线：
    /// 快照链激活；RemotePlayerSync 双轨退役，广播统一走 WorldSnapshot）。
    /// 主循环线程调用（ProcessPacket）。
    /// </summary>
    public class PositionSyncHandler
    {
        private readonly Server3C _server;
        private readonly ILogger _logger;
        private readonly EntityStore _entityStore;

        // Server3C 注入（LoginHandler 同款惯例——项目既有模式，不用委托 seam）
        public PositionSyncHandler(Server3C server, ILogger logger, EntityStore entityStore)
        {
            _server = server;
            _logger = logger;
            _entityStore = entityStore;
        }

        /// <summary>核心校验+应用（可独立测试——不依赖 Server3C 运行时）</summary>
        public MoveValidationResult ValidateAndApply(PositionSyncRequest request, PlayerState player)
        {
            var result = MovementValidator.Validate(
                new Vec3(player.Position.X, player.Position.Y, player.Position.Z),
                new Vec3(request.X, request.Y, request.Z),
                request.Rotation, request.Speed,
                player.LastUpdateTimestamp, request.Timestamp);

            // 守卫回退：非有限输入时 validator 原样透传 rotation——回退到上次已知有限值，
            // 防 NaN 旋转进 store → ack → 快照广播（守卫注释口径：杜绝 NaN 经广播扩散）
            float authoritativeRotation = float.IsNaN(result.AuthoritativeRotation) || float.IsInfinity(result.AuthoritativeRotation)
                ? player.Rotation
                : result.AuthoritativeRotation;

            player.UpdatePosition(
                new Vector3(result.AuthoritativePosition.X, result.AuthoritativePosition.Y, result.AuthoritativePosition.Z),
                authoritativeRotation,
                result.ClampedSpeed,
                request.Timestamp, request.Sequence);
            // UpdatePosition 已置 IsDirty（P1 T4）——WorldSnapshot 链自动携带

            if (result.NeedsCorrection)
            {
                if (result.ClampedSpeed != request.Speed)
                    _logger.LogWarning($"Player {player.PlayerId} speed too high: {request.Speed} > {MovementValidator.MaxSpeed}, clamped to {result.ClampedSpeed}");
                else
                    _logger.LogWarning($"Player {player.PlayerId} position corrected: teleport/jump outside plausibility limits");
            }

            return result;
        }

        public async Task HandlePositionSyncAsync(KcpServerSession session, PositionSyncRequest request)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue)
            {
                _logger.LogWarning($"PositionSync from unknown session {session.SessionId}");
                return;
            }

            var player = _entityStore.GetPlayer(playerId.Value);
            if (player == null)
            {
                _logger.LogWarning($"Player {playerId} entity not found");
                return;
            }

            var result = ValidateAndApply(request, player);

            var response = new PositionSyncResponse
            {
                AcknowledgedSequence = request.Sequence,
                ServerTimestamp = DateTime.UtcNow.Ticks,
                HasPositionCorrection = result.NeedsCorrection,
                AuthoritativeX = result.AuthoritativePosition.X,
                AuthoritativeY = result.AuthoritativePosition.Y,
                AuthoritativeZ = result.AuthoritativePosition.Z,
                // ack 同守卫（ValidateAndApply 已落库回退值——回读 player.Rotation 即同值）：
                // 防 NaN 旋转进纠偏 ack，污染客户端纠偏链路
                AuthoritativeRotation = float.IsNaN(result.AuthoritativeRotation) || float.IsInfinity(result.AuthoritativeRotation)
                    ? player.Rotation
                    : result.AuthoritativeRotation
            };

            await _server.SendToSessionAsync(session, MessageId.PositionSyncResponse, response);
        }
    }
}

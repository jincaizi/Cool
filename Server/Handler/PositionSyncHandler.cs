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

        /// <summary>服务端认可的地面高度（与 CastHandler 同一 navmesh 闭包）；null = 无导航宿主，
        /// 跳跃接地/高度不额外钳制（沿用客户端自报）。</summary>
        public Func<Vec3, float?>? GroundHeight { get; set; }

        public void SetGroundHeight(Func<Vec3, float?>? groundHeight) => GroundHeight = groundHeight;

        /// <summary>核心校验+应用（可独立测试——不依赖 Server3C 运行时）</summary>
        public MoveValidationResult? ValidateAndApply(PositionSyncRequest request, PlayerState player)
            => ValidateAndApply(request, player, _server.CurrentTick);

        public MoveValidationResult? ValidateAndApply(PositionSyncRequest request, PlayerState player, uint nowTick)
        {
            if (player.SupportsAuthoritativeRespawn && (!player.IsAlive || request.RespawnTick != player.RespawnTick))
                return null;

            if (player.StrictMovementBudget)
                return ValidateWithBudget(request, player, nowTick);

            // —— 既有路径保持原样（旧客户端/未协商能力位）——
            // 第一帧以一个上报周期校验位移，避免把死亡等待时间算进移动预算。
            bool firstReportAfterRespawn = player.RespawnTick.HasValue && player.LastUpdateTimestamp == 0L;
            var result = MovementValidator.Validate(
                new Vec3(player.Position.X, player.Position.Y, player.Position.Z),
                new Vec3(request.X, request.Y, request.Z),
                request.Rotation, request.Speed,
                player.LastUpdateTimestamp,
                firstReportAfterRespawn ? Server3C.TickIntervalMs * TimeSpan.TicksPerMillisecond : request.Timestamp);

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
            // UpdatePosition 置 IsDirty——保留（P2 测试语义）；AOI 广播按带节拍直接读当前位置，不依赖脏标记

            // 防御姿态随同一次位置同步刷新（格挡减伤服务端化的输入侧）。**故意不进 MovementValidator**：
            // 位置校验与防御无关；防御的信任面（客户端上报 + 服务端几何/耐久校验）登记见 CombatRules.IsBlocked
            if (player.IsDefending != request.IsDefending)
            {
                player.IsDefending = request.IsDefending;
                player.BumpCombatVersion();
            }

            if (result.NeedsCorrection)
            {
                if (result.ClampedSpeed != request.Speed)
                    _logger.LogWarning($"Player {player.PlayerId} speed too high: {request.Speed} > {MovementValidator.MaxSpeed}, clamped to {result.ClampedSpeed}");
                else
                    _logger.LogWarning($"Player {player.PlayerId} position corrected: teleport/jump outside plausibility limits");
            }

            return result;
        }

        private MoveValidationResult ValidateWithBudget(PositionSyncRequest request, PlayerState player, uint nowTick)
        {
            if (!float.IsFinite(request.X) || !float.IsFinite(request.Y) || !float.IsFinite(request.Z)
                || !float.IsFinite(request.Rotation) || !float.IsFinite(request.Speed))
                return CorrectionToCurrent(player);

            if (request.LifeId != 0 && request.LifeId != player.LifeId)
                return CorrectionToCurrent(player);

            if (request.ClientControlVersion < player.ControlVersion)
                return CorrectionToCurrent(player);

            float dt = player.Movement.LastBudgetTick == 0
                ? Server3C.TickIntervalMs / 1000f
                : MovementBudgetMath.ClampDeltaSeconds(nowTick, player.Movement.LastBudgetTick);
            player.Movement.LastBudgetTick = nowTick;

            float normal = player.ControlActionLock
                ? 0f
                : MovementBudgetMath.NormalDistance(request.LocomotionMode, request.IsDefending,
                    player.ControlSpeedScale, dt);

            float dash = GrantDash(request, player, nowTick, dt);
            float jump = UpdateJump(request, player, nowTick, dt);

            float allowed = normal + dash + jump;
            float y = ClampY(request, player, nowTick);
            var clamped = MovementBudgetMath.ClampHorizontal(
                new Vec3(player.Position.X, player.Position.Y, player.Position.Z),
                new Vec3(request.X, y, request.Z), allowed);

            bool corrected = clamped.X != request.X || clamped.Y != request.Y || clamped.Z != request.Z;
            float speed = Math.Clamp(request.Speed, 0f, MovementValidator.MaxSpeed);

            player.UpdatePosition(new Vector3(clamped.X, clamped.Y, clamped.Z),
                request.Rotation, speed, request.Timestamp, request.Sequence);
            if (player.IsDefending != request.IsDefending)
            {
                player.IsDefending = request.IsDefending;
                player.BumpCombatVersion();
            }

            return new MoveValidationResult(corrected, clamped, request.Rotation, speed);
        }

        private static MoveValidationResult CorrectionToCurrent(PlayerState player)
            => new MoveValidationResult(true,
                new Vec3(player.Position.X, player.Position.Y, player.Position.Z), player.Rotation, 0f);

        private float GrantDash(PositionSyncRequest request, PlayerState player, uint nowTick, float dt)
        {
            var window = player.Displacement;
            if (window == null || request.CastId != window.CastId
                || request.DisplacementSkillId != window.SkillId
                || nowTick < window.StartTick || nowTick >= window.EndTick)
                return 0f;

            float remaining = window.TotalDistance
                + window.Speed * MovementBudgetMath.OneTickSeconds
                - window.GrantedDistance;
            float granted = MovementBudgetMath.DashDistance(
                window.Speed, player.ControlSpeedScale, dt, remaining);
            window.GrantedDistance += granted;
            return granted;
        }

        private float UpdateJump(PositionSyncRequest request, PlayerState player, uint nowTick, float dt)
        {
            var m = player.Movement;
            var jump = _server.ElementalCatalog.Jump;
            float maxAir = 2f * jump.MaxRiseSeconds;

            if (request.JumpPhase == PlayerJumpPhase.Start && request.JumpSeq != 0
                && request.JumpSeq != m.LastJumpSeq)
            {
                m.LastJumpSeq = request.JumpSeq;
                // 麻痹禁止新起跳；无导航宿主沿用客户端自报（IsGrounded 默认 true）。
                if (!player.ControlActionLock && IsGrounded(request))
                {
                    m.JumpActive = true;
                    m.JumpStartTick = nowTick;
                    m.JumpStartY = player.Position.Y;
                    // 用本次上报速度但按冲刺上限封顶：客户端不能靠伪造 Speed>8 换更远惯性。
                    m.JumpHorizontalSpeed = Math.Clamp(request.Speed, 0f, CombatRules.SprintSpeed);
                    m.JumpGrantedDistance = 0f;
                }
                else
                {
                    m.JumpActive = false;
                }
            }
            else if (request.JumpPhase == PlayerJumpPhase.End)
            {
                m.JumpActive = false;
            }

            if (!m.JumpActive) return 0f;

            // 起跳包只记录，不把起跳前的间隔算成空中惯性。
            if (nowTick == m.JumpStartTick) return 0f;

            float elapsed = (nowTick - m.JumpStartTick) / CombatRules.TickRateHz;
            if (elapsed > maxAir) { m.JumpActive = false; return 0f; }

            // 普通空中移动由 normal 预算覆盖；只有麻痹/动作锁才额外发跳跃惯性，
            // 避免同一段位移被 normal + inertia 双算。
            if (!player.ControlActionLock) return 0f;

            float remaining = m.JumpHorizontalSpeed * maxAir - m.JumpGrantedDistance;
            float granted = MovementBudgetMath.JumpInertiaDistance(
                m.JumpHorizontalSpeed, dt, remaining);
            m.JumpGrantedDistance += granted;
            return granted;
        }

        private bool IsGrounded(PositionSyncRequest request)
        {
            var ground = GroundHeight?.Invoke(new Vec3(request.X, request.Y, request.Z));
            // 出生点 Y=0.3（胶囊中心），navmesh 地面为 0；0.5m 容差覆盖皮肤宽度/台阶。
            return !ground.HasValue || request.Y <= ground.Value + 0.5f;
        }

        private float ClampY(PositionSyncRequest request, PlayerState player, uint nowTick)
        {
            var ground = GroundHeight?.Invoke(new Vec3(request.X, request.Y, request.Z));
            if (!ground.HasValue) return request.Y;

            float maxY = ground.Value + MovementValidator.MaxJumpHeight;
            if (player.Movement.JumpActive)
            {
                float elapsed = (nowTick - player.Movement.JumpStartTick) / CombatRules.TickRateHz;
                var jump = _server.ElementalCatalog.Jump;
                float arc = MovementBudgetMath.MaxJumpY(
                    player.Movement.JumpStartY, jump.JumpForce, jump.Gravity, elapsed);
                maxY = Math.Min(maxY, arc + 0.05f);
            }
            return Math.Clamp(request.Y, ground.Value - 1f, maxY);
        }

        public async Task HandlePositionSyncAsync(IServerSession session, PositionSyncRequest request)
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

            if (ValidateAndApply(request, player, _server.CurrentTick) is not MoveValidationResult result) return;

            var response = new PositionSyncResponse
            {
                AcknowledgedSequence = request.Sequence,
                ServerTimestamp = DateTime.UtcNow.Ticks,
                HasPositionCorrection = result.NeedsCorrection,
                RespawnTick = player.RespawnTick,
                ControlVersion = player.ControlVersion,
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

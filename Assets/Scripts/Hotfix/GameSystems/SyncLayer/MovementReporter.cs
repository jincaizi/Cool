using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpNet;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 移动上报器 —— 本地角色即时模拟后的节流上报（spec B+）：
    /// 每 ServerTickRateMs 上报一次 PositionSyncRequest（seq 递增）；
    /// 预测帧交共享 PredictionReconciler；响应驱动纠偏查询。
    /// 驱动点：ClientNetworkManager.Update（宿主每帧调）——与测试宿主解耦，
    /// 不依赖真实 NetMovementPolicy。
    /// 注入参数 injectedSendDelayMs 仅测试用（PlayMode 延迟注入验收；生产恒 0）。
    /// </summary>
    public sealed class MovementReporter
    {
        private readonly INetworkClient _client;
        private readonly Func<Vec3> _positionProvider;
        private readonly Func<float> _rotationProvider;
        private readonly Func<float> _speedProvider;
        private readonly int _injectedSendDelayMs;
        private readonly PredictionReconciler _reconciler = new PredictionReconciler();
        private float _timer;
        private uint _sequence;

        /// <summary>最近一次上报的服务器时钟（纠偏用）</summary>
        public long LastServerTimestamp { get; private set; }

        public MovementReporter(INetworkClient client,
            Func<Vec3> positionProvider, Func<float> rotationProvider, Func<float> speedProvider,
            int injectedSendDelayMs = 0)
        {
            _client = client;
            _positionProvider = positionProvider;
            _rotationProvider = rotationProvider;
            _speedProvider = speedProvider;
            _injectedSendDelayMs = injectedSendDelayMs;
        }

        /// <summary>每帧驱动；reportIntervalSeconds = ServerTickRateMs/1000（默认 66ms）</summary>
        public void Tick(float deltaSeconds, float reportIntervalSeconds)
        {
            _timer -= deltaSeconds;
            if (_timer > 0f) return;

            _timer = reportIntervalSeconds;
            Report();
        }

        public void Report()
        {
            if (!_client.IsConnected) return;

            var pos = _positionProvider();
            float rot = _rotationProvider();
            uint seq = ++_sequence;

            _reconciler.RecordFrame(seq, pos, rot);

            // 测试钩子：注入上行延迟（PlayMode 延迟注入验收用；生产恒 0）
            if (_injectedSendDelayMs > 0)
                System.Threading.Thread.Sleep(_injectedSendDelayMs);

            _client.Send(MessageId.PositionSyncRequest, new PositionSyncRequest
            {
                X = pos.X, Y = pos.Y, Z = pos.Z,
                Rotation = rot,
                Speed = _speedProvider(),
                Timestamp = DateTime.UtcNow.Ticks,
                Sequence = seq
            });
        }

        /// <summary>收到 PositionSyncResponse 时调用——返回是否需要纠偏（超阈值）</summary>
        public bool OnResponse(PositionSyncResponse response)
        {
            LastServerTimestamp = response.ServerTimestamp;
            _reconciler.Prune(response.AcknowledgedSequence);
            return _reconciler.TryGetCorrection(
                response.AcknowledgedSequence,
                new Vec3(response.AuthoritativeX, response.AuthoritativeY, response.AuthoritativeZ),
                response.AuthoritativeRotation,
                out _targetPosition, out _targetRotation);
        }

        /// <summary>
        /// 最近一次纠偏目标（仅 OnResponse 返回 true 时更新）——不代表"当前服务器权威目标"；
        /// 只在 OnResponse 返回 true 后读取，其余时刻为上次残留值。
        /// </summary>
        public Vec3 LastCorrectionTarget => _targetPosition;
        public float LastCorrectionRotation => _targetRotation;

        private Vec3 _targetPosition;
        private float _targetRotation;
    }
}

using System.Collections.Generic;

namespace Cool.SyncCore
{
    /// <summary>
    /// 预测帧对账（spec B+：客户端预测 + 服务端校验）——
    /// 记录本地预测帧（seq → 位置/旋转），收到 ack 后按 seq 比对：
    /// 偏差 ≤ 阈值忽略；超阈值返回服务器权威目标（由客户端执行平滑拉回）。
    /// 纯 C# 共享；帧上限 60（1s @66ms 上报）。
    /// 非线程安全：RecordFrame（上报路径）与 TryGetCorrection/Prune（ack 路径）必须同一线程串行调用（P2 中经 Task 4 主线程封送）；
    /// seq 按每会话单调递增假设（无 wrap；重连应新建实例）。
    /// </summary>
    public sealed class PredictionReconciler
    {
        public const float PositionThreshold = 0.5f;
        public const float RotationThresholdDegrees = 5f;
        public const int MaxFrames = 60;

        private readonly Dictionary<uint, PredictedFrame> _frames = new Dictionary<uint, PredictedFrame>();

        public int FrameCount => _frames.Count;

        /// <summary>记录一帧本地预测（上报时调用）</summary>
        public void RecordFrame(uint sequence, Vec3 position, float rotationY)
        {
            _frames[sequence] = new PredictedFrame { Position = position, RotationY = rotationY };

            // 上限保护：超限清最旧（按 seq 最小）
            if (_frames.Count > MaxFrames)
            {
                uint oldest = uint.MaxValue;
                foreach (var seq in _frames.Keys)
                    if (seq < oldest) oldest = seq;
                _frames.Remove(oldest);
            }
        }

        /// <summary>
        /// 用服务器 ack 比对预测帧。返回 true 表示需要纠偏（out 参数为服务器权威目标）。
        /// 无对应帧或偏差 ≤ 阈值返回 false。
        /// </summary>
        public bool TryGetCorrection(uint serverSequence, Vec3 serverPosition, float serverRotationY,
            out Vec3 targetPosition, out float targetRotationY)
        {
            targetPosition = serverPosition;
            targetRotationY = serverRotationY;

            // 非有限 ack（损坏/恶意包）——忽略该帧（保持本地预测），防 NaN 目标污染拉回
            if (!IsFinite(serverPosition) || !IsFinite(serverRotationY))
                return false;

            if (!_frames.TryGetValue(serverSequence, out var predicted))
                return false;

            float posDeviation = predicted.Position.Distance(serverPosition);
            float rotDeviation = AngleDelta(predicted.RotationY, serverRotationY);

            return posDeviation > PositionThreshold || rotDeviation > RotationThresholdDegrees;
        }

        /// <summary>清理 seq &lt; minSeq 的旧帧（收到新 ack 时调用，控制内存）</summary>
        public void Prune(uint minSeq)
        {
            var stale = new List<uint>();
            foreach (var kv in _frames)
                if (kv.Key < minSeq) stale.Add(kv.Key);
            foreach (var seq in stale)
                _frames.Remove(seq);
        }

        private static bool IsFinite(Vec3 v)
            => !float.IsNaN(v.X) && !float.IsInfinity(v.X)
            && !float.IsNaN(v.Y) && !float.IsInfinity(v.Y)
            && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);

        private static bool IsFinite(float v)
            => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float AngleDelta(float a, float b)
        {
            float delta = (b - a) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta < 0f ? -delta : delta;
        }

        private struct PredictedFrame
        {
            public Vec3 Position;
            public float RotationY;
        }
    }
}

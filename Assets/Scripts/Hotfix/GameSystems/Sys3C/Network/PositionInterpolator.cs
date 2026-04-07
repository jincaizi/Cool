using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 其他玩家位置插值器
    /// </summary>
    public class PositionInterpolator
    {
        // 插值目标数据
        private class InterpolateTarget
        {
            public Vector3 CurrentPosition;
            public Quaternion CurrentRotation;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public float InterpolationTime;     // 目标到达时间
            public long LastUpdateTimestamp;
            public float NetworkLatency;        // 估算的网络延迟
        }

        private readonly Dictionary<long, InterpolateTarget> _targets = new Dictionary<long, InterpolateTarget>();

        // 插值延迟（秒），服务端广播间隔约 0.1s (10Hz)，加上延迟补偿
        private const float INTERPOLATION_DELAY = 0.1f;

        /// <summary>
        /// 更新目标位置（服务端广播触发）
        /// </summary>
        public void UpdateTarget(long playerId, Vector3 position, Quaternion rotation, long serverTimestamp)
        {
            if (!_targets.TryGetValue(playerId, out var target))
            {
                target = new InterpolateTarget
                {
                    CurrentPosition = position,
                    CurrentRotation = rotation,
                    TargetPosition = position,
                    TargetRotation = rotation
                };
                _targets[playerId] = target;
            }

            long now = System.DateTime.UtcNow.Ticks;
            long elapsed = now - serverTimestamp;
            float elapsedSeconds = elapsed / (float)System.TimeSpan.TicksPerSecond;

            // 网络延迟估算（简化：使用实际经过时间作为延迟）
            target.NetworkLatency = elapsedSeconds;

            // 目标位置 = 刚刚收到的服务端位置
            // 插值时间 = 延迟 + 一个广播间隔
            target.TargetPosition = position;
            target.TargetRotation = rotation;
            target.InterpolationTime = elapsedSeconds + INTERPOLATION_DELAY;
            target.LastUpdateTimestamp = serverTimestamp;
        }

        /// <summary>
        /// 每帧获取插值后的位置（供渲染使用）
        /// </summary>
        public (Vector3 position, Quaternion rotation) GetInterpolatedState(long playerId)
        {
            if (!_targets.TryGetValue(playerId, out var target))
                return (Vector3.zero, Quaternion.identity);

            long now = System.DateTime.UtcNow.Ticks;
            float t = (now - target.LastUpdateTimestamp) / (float)System.TimeSpan.TicksPerSecond;

            // t 是从上一个更新到现在经过的时间
            // 使用 t / INTERPOLATION_DELAY 作为 Lerp 参数
            float lerpT = Mathf.Clamp01(t / INTERPOLATION_DELAY);

            return (
                Vector3.Lerp(target.CurrentPosition, target.TargetPosition, lerpT),
                Quaternion.Slerp(target.CurrentRotation, target.TargetRotation, lerpT)
            );
        }

        /// <summary>
        /// 每帧推进插值（应在 LateUpdate 中调用）
        /// </summary>
        public void FrameAdvance(long playerId)
        {
            if (!_targets.TryGetValue(playerId, out var target))
                return;

            long now = System.DateTime.UtcNow.Ticks;
            float elapsed = (now - target.LastUpdateTimestamp) / (float)System.TimeSpan.TicksPerSecond;
            float lerpT = Mathf.Clamp01(elapsed / INTERPOLATION_DELAY);

            target.CurrentPosition = Vector3.Lerp(target.CurrentPosition, target.TargetPosition, lerpT);
            target.CurrentRotation = Quaternion.Slerp(target.CurrentRotation, target.TargetRotation, lerpT);
        }

        /// <summary>
        /// 移除目标
        /// </summary>
        public void RemoveTarget(long playerId)
        {
            _targets.Remove(playerId);
        }
    }
}
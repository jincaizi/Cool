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
            public long LastUpdateTimestamp;
        }

        private readonly Dictionary<long, InterpolateTarget> _targets = new Dictionary<long, InterpolateTarget>();

        // 插值延迟（秒），服务端广播间隔约 0.1s (10Hz)，加上延迟补偿
        private const float INTERPOLATION_DELAY = 0.1f;

        private static float TicksToSeconds(long ticks) => ticks / (float)System.TimeSpan.TicksPerSecond;

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

            target.TargetPosition = position;
            target.TargetRotation = rotation;
            target.LastUpdateTimestamp = serverTimestamp;
        }

        /// <summary>
        /// 每帧获取插值后的位置（供渲染使用）
        /// </summary>
        public (Vector3 position, Quaternion rotation) GetInterpolatedState(long playerId)
        {
            if (!_targets.TryGetValue(playerId, out var target))
                return (Vector3.zero, Quaternion.identity);

            float lerpT = GetLerpT(target.LastUpdateTimestamp);

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

            float lerpT = GetLerpT(target.LastUpdateTimestamp);

            target.CurrentPosition = Vector3.Lerp(target.CurrentPosition, target.TargetPosition, lerpT);
            target.CurrentRotation = Quaternion.Slerp(target.CurrentRotation, target.TargetRotation, lerpT);
        }

        private float GetLerpT(long lastUpdateTimestamp)
        {
            long now = System.DateTime.UtcNow.Ticks;
            float elapsed = TicksToSeconds(now - lastUpdateTimestamp);
            return Mathf.Clamp01(elapsed / INTERPOLATION_DELAY);
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
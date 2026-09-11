using System;

namespace KcpServer.Monster
{
    /// <summary>
    /// 怪物感知检测（旧 Server/AI 移植）——yaw 语义与 HitResolver 一致（yaw=0 → +Z）。
    /// 视野锥 = 前方 ±visionAngle/2（水平）。贴脸（dist≈0）必感知——零向量退化同 D-P4-2a 语义。
    /// </summary>
    public static class TargetDetector
    {
        public static bool CanDetect(float yawDeg, Vector3 aiPosition, Vector3 targetPosition,
            float detectionRadius, float visionAngle)
        {
            float dx = targetPosition.X - aiPosition.X;
            float dz = targetPosition.Z - aiPosition.Z;
            float distSqr = dx * dx + dz * dz;
            if (distSqr > detectionRadius * detectionRadius) return false;
            if (distSqr < 0.0001f) return true;

            float forwardX = (float)Math.Sin(yawDeg * Math.PI / 180.0);
            float forwardZ = (float)Math.Cos(yawDeg * Math.PI / 180.0);
            float dot = (forwardX * dx + forwardZ * dz) / (float)Math.Sqrt(distSqr);
            float halfCos = (float)Math.Cos(visionAngle * 0.5f * Math.PI / 180.0);
            return dot >= halfCos;
        }
    }
}

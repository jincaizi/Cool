using KcpServer;

namespace KcpServer.AI.Detection
{
    public class TargetDetector
    {
        private readonly float _detectionRadius;
        private readonly float _visionAngle;

        public TargetDetector(float detectionRadius, float visionAngle)
        {
            _detectionRadius = detectionRadius;
            _visionAngle = visionAngle;
        }

        public bool CanDetectTarget(Vector3 aiPosition, Quaternion aiRotation, Vector3 targetPosition)
        {
            // Distance check
            float distance = Vector3.Distance(aiPosition, targetPosition);
            if (distance > _detectionRadius) return false;

            // Vision cone check - get forward direction from rotation
            Vector3 aiForward = GetForward(aiRotation);
            Vector3 directionToTarget = targetPosition - aiPosition;

            // Flatten Y for 2D angle check (we're checking horizontal vision cone)
            aiForward.Y = 0;
            directionToTarget.Y = 0;

            aiForward = aiForward.Normalize();
            directionToTarget = directionToTarget.Normalize();

            float angle = AngleBetween(aiForward, directionToTarget);
            if (angle > _visionAngle / 2) return false;

            return true;
        }

        private static Vector3 GetForward(Quaternion rotation)
        {
            // Convert quaternion to forward direction
            // For a Y-axis rotation (yaw), forward is (sin(y), 0, cos(y)) where Y is yaw angle
            // Since our Quaternion stores X,Y,Z,W directly, we compute:
            float siny = 2 * (rotation.W * rotation.Y + rotation.X * rotation.Z);
            float cosy = 1 - 2 * (rotation.X * rotation.X + rotation.Y * rotation.Y);
            return new Vector3(siny, 0, cosy);
        }

        private static float AngleBetween(Vector3 a, Vector3 b)
        {
            float dot = a.X * b.X + a.Z * b.Z; // Use XZ for horizontal
            float det = a.X * b.Z - a.Z * b.X;
            float angle = (float)System.Math.Atan2(det, dot);
            return (float)System.Math.Abs(angle * 180.0f / System.Math.PI);
        }
    }

    public static class Vector3Extensions
    {
        public static Vector3 Normalize(this Vector3 v)
        {
            float mag = (float)System.Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (mag < 0.0001f) return new Vector3(0, 0, 0);
            return new Vector3(v.X / mag, v.Y / mag, v.Z / mag);
        }
    }
}
using System;

namespace KcpServer.Monster
{
    /// <summary>简单平面移动（旧 Server/AI 移植——Quaternion 数学与 Server/Util 同源）</summary>
    public sealed class SimpleMoveSystem
    {
        private const float RotationSpeed = 180f; // 度/秒

        public void MoveTo(MonsterState m, Vector3 targetPosition, float deltaTime)
        {
            Vector3 direction = targetPosition - m.Position;
            direction.Y = 0;

            if (direction.X * direction.X + direction.Z * direction.Z < 0.001f) return;

            direction = direction.Normalize();

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            m.Rotation = RotateTowards(m.Rotation, targetRotation, RotationSpeed * deltaTime);

            Vector3 movement = direction * m.Config.moveSpeed * deltaTime;
            m.Position = m.Position + movement;
        }

        public bool HasReached(MonsterState m, Vector3 target, float threshold = 0.1f)
        {
            float dx = m.Position.X - target.X;
            float dz = m.Position.Z - target.Z;
            return (dx * dx + dz * dz) < (threshold * threshold);
        }

        private static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegrees)
        {
            float angle = Angle(from, to);
            if (angle < 0.0001f) return to;

            float t = (float)Math.Min(1, maxDegrees / angle);
            return Slerp(from, to, t);
        }

        private static float Angle(Quaternion a, Quaternion b)
        {
            float dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            return (float)(Math.Acos(Math.Min(Math.Abs(dot), 1.0)) * 2.0 * 180.0 / Math.PI);
        }

        private static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            float dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;

            Quaternion tb = b;
            if (dot < 0)
            {
                tb = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);
                dot = -dot;
            }

            if (dot > 0.9995f)
            {
                return new Quaternion(
                    a.X + (tb.X - a.X) * t,
                    a.Y + (tb.Y - a.Y) * t,
                    a.Z + (tb.Z - a.Z) * t,
                    a.W + (tb.W - a.W) * t
                ).Normalize();
            }

            float theta0 = (float)Math.Acos(dot);
            float theta = theta0 * t;
            float sinTheta = (float)Math.Sin(theta);
            float sinTheta0 = (float)Math.Sin(theta0);

            float s0 = (float)(Math.Cos(theta) - dot * sinTheta / sinTheta0);
            float s1 = (float)(sinTheta / sinTheta0);

            return new Quaternion(
                s0 * a.X + s1 * tb.X,
                s0 * a.Y + s1 * tb.Y,
                s0 * a.Z + s1 * tb.Z,
                s0 * a.W + s1 * tb.W
            );
        }
    }

    /// <summary>Vector3 归一化扩展（旧 Server/AI 同款——Server/Util/Vector3 无 Normalize 成员）</summary>
    public static class Vector3Extensions
    {
        public static Vector3 Normalize(this Vector3 v)
        {
            float mag = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (mag < 0.0001f) return new Vector3(0, 0, 0);
            return new Vector3(v.X / mag, v.Y / mag, v.Z / mag);
        }
    }
}

using KcpServer;
using KcpServer.AI.Core;

namespace KcpServer.AI.Movement
{
    public class SimpleMoveSystem
    {
        private const float RotationSpeed = 180f;

        public void MoveTo(AiComponent ai, Vector3 targetPosition, float deltaTime)
        {
            Vector3 direction = targetPosition - ai.Position;
            direction.Y = 0;

            if (direction.X * direction.X + direction.Z * direction.Z < 0.001f) return;

            direction = direction.Normalize();

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            ai.Rotation = RotateTowards(ai.Rotation, targetRotation, RotationSpeed * deltaTime);

            Vector3 movement = direction * ai.MoveSpeed * deltaTime;
            ai.Position = ai.Position + movement;
        }

        public bool HasReached(AiComponent ai, Vector3 target, float threshold = 0.1f)
        {
            float dx = ai.Position.X - target.X;
            float dz = ai.Position.Z - target.Z;
            return (dx * dx + dz * dz) < (threshold * threshold);
        }

        private static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegrees)
        {
            float angle = Angle(from, to);
            if (angle < 0.0001f) return to;

            float t = System.Math.Min(1, maxDegrees / angle);
            return Slerp(from, to, t);
        }

        private static float Angle(Quaternion a, Quaternion b)
        {
            float dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            return (float)(System.Math.Acos(System.Math.Min(System.Math.Abs(dot), 1.0)) * 2.0 * 180.0 / System.Math.PI);
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

            float theta0 = (float)System.Math.Acos(dot);
            float theta = theta0 * t;
            float sinTheta = (float)System.Math.Sin(theta);
            float sinTheta0 = (float)System.Math.Sin(theta0);

            float s0 = (float)(System.Math.Cos(theta) - dot * sinTheta / sinTheta0);
            float s1 = (float)(sinTheta / sinTheta0);

            return new Quaternion(
                s0 * a.X + s1 * tb.X,
                s0 * a.Y + s1 * tb.Y,
                s0 * a.Z + s1 * tb.Z,
                s0 * a.W + s1 * tb.W
            );
        }
    }
}

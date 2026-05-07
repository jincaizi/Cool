using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeGizmos
    {
        public static bool Enabled = true;
        public static Color HitColor = Color.red;
        public static Color MissColor = Color.green;
        private const int ARC_SEGMENTS = 24;

        public static void DrawCone(Vector3 origin, Vector3 forward, float range, float angle)
        {
            if (!Enabled) return;
            float halfAngle = angle * 0.5f;
            Gizmos.color = MissColor;
            DrawArc(origin, forward, range, -halfAngle, halfAngle);
            Vector3 left = Quaternion.Euler(0, -halfAngle, 0) * forward * range;
            Vector3 right = Quaternion.Euler(0, halfAngle, 0) * forward * range;
            Gizmos.DrawLine(origin, origin + left);
            Gizmos.DrawLine(origin, origin + right);
        }

        public static void DrawSector(Vector3 origin, Vector3 forward, float range,
            float angleStart, float angleEnd)
        {
            if (!Enabled) return;
            Gizmos.color = MissColor;
            DrawArc(origin, forward, range, angleStart, angleEnd);
            Vector3 startDir = Quaternion.Euler(0, angleStart, 0) * forward * range;
            Vector3 endDir = Quaternion.Euler(0, angleEnd, 0) * forward * range;
            Gizmos.DrawLine(origin, origin + startDir);
            Gizmos.DrawLine(origin, origin + endDir);
        }

        public static void DrawCircle(Vector3 origin, float radius)
        {
            if (!Enabled) return;
            Gizmos.color = MissColor;
            Vector3 prev = origin + Vector3.forward * radius;
            for (int i = 1; i <= ARC_SEGMENTS; i++)
            {
                float angle = i * 360f / ARC_SEGMENTS * Mathf.Deg2Rad;
                Vector3 next = origin + new Vector3(
                    Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        public static void DrawRect(Vector3 origin, Vector3 forward, float range, float width)
        {
            if (!Enabled) return;
            Gizmos.color = MissColor;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 farCenter = origin + forward * range;
            Vector3 halfW = right * (width * 0.5f);
            Gizmos.DrawLine(origin - halfW, origin + halfW);
            Gizmos.DrawLine(farCenter - halfW, farCenter + halfW);
            Gizmos.DrawLine(origin - halfW, farCenter - halfW);
            Gizmos.DrawLine(origin + halfW, farCenter + halfW);
        }

        public static void DrawHit(Vector3 position)
        {
            if (!Enabled) return;
            Gizmos.color = HitColor;
            Gizmos.DrawWireSphere(position, 0.3f);
        }

        private static void DrawArc(Vector3 origin, Vector3 forward, float radius,
            float startAngle, float endAngle)
        {
            float span = endAngle - startAngle;
            int steps = Mathf.Max(2, Mathf.RoundToInt(ARC_SEGMENTS * (span / 360f)));
            Vector3 prev = origin + Quaternion.Euler(0, startAngle, 0) * forward * radius;
            for (int i = 1; i <= steps; i++)
            {
                float angle = startAngle + (i / (float)steps) * span;
                Vector3 next = origin + Quaternion.Euler(0, angle, 0) * forward * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}

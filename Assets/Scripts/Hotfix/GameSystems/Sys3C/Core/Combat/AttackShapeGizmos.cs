using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeGizmos
    {
        public static bool Enabled = true;
        public static Color HitColor = Color.red;
        public static Color MissColor = Color.green;
        private const int ARC_SEGMENTS = 24;

        public static void DrawCone(Vector3 origin, Vector3 forward, float range, float angle, float duration = 0f)
        {
            if (!Enabled) return;
            float halfAngle = angle * 0.5f;
            DrawArc(origin, forward, range, -halfAngle, halfAngle, MissColor, duration);
            Vector3 left = Quaternion.Euler(0, -halfAngle, 0) * forward * range;
            Vector3 right = Quaternion.Euler(0, halfAngle, 0) * forward * range;
            UnityEngine.Debug.DrawLine(origin, origin + left, MissColor, duration);
            UnityEngine.Debug.DrawLine(origin, origin + right, MissColor, duration);
        }

        public static void DrawSector(Vector3 origin, Vector3 forward, float range,
            float angleStart, float angleEnd, float duration = 0f)
        {
            if (!Enabled) return;
            DrawArc(origin, forward, range, angleStart, angleEnd, MissColor, duration);
            Vector3 startDir = Quaternion.Euler(0, angleStart, 0) * forward * range;
            Vector3 endDir = Quaternion.Euler(0, angleEnd, 0) * forward * range;
            UnityEngine.Debug.DrawLine(origin, origin + startDir, MissColor, duration);
            UnityEngine.Debug.DrawLine(origin, origin + endDir, MissColor, duration);
        }

        public static void DrawCircle(Vector3 origin, float radius, float duration = 0f)
        {
            if (!Enabled) return;
            Vector3 prev = origin + Vector3.forward * radius;
            for (int i = 1; i <= ARC_SEGMENTS; i++)
            {
                float angle = i * 360f / ARC_SEGMENTS * Mathf.Deg2Rad;
                Vector3 next = origin + new Vector3(
                    Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
                UnityEngine.Debug.DrawLine(prev, next, MissColor, duration);
                prev = next;
            }
        }

        public static void DrawRect(Vector3 origin, Vector3 forward, float range, float width, float duration = 0f)
        {
            if (!Enabled) return;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 farCenter = origin + forward * range;
            Vector3 halfW = right * (width * 0.5f);
            UnityEngine.Debug.DrawLine(origin - halfW, origin + halfW, MissColor, duration);
            UnityEngine.Debug.DrawLine(farCenter - halfW, farCenter + halfW, MissColor, duration);
            UnityEngine.Debug.DrawLine(origin - halfW, farCenter - halfW, MissColor, duration);
            UnityEngine.Debug.DrawLine(origin + halfW, farCenter + halfW, MissColor, duration);
        }

        public static void DrawHit(Vector3 position, float duration = 0f)
        {
            if (!Enabled) return;
            float r = 0.3f;
            Vector3 prev = position + Vector3.forward * r;
            for (int i = 1; i <= 8; i++)
            {
                float angle = i * 360f / 8 * Mathf.Deg2Rad;
                Vector3 next = position + new Vector3(Mathf.Sin(angle) * r, 0, Mathf.Cos(angle) * r);
                UnityEngine.Debug.DrawLine(prev, next, HitColor, duration);
                prev = next;
            }
        }

        private static void DrawArc(Vector3 origin, Vector3 forward, float radius,
            float startAngle, float endAngle, Color color, float duration)
        {
            float span = endAngle - startAngle;
            int steps = Mathf.Max(2, Mathf.RoundToInt(ARC_SEGMENTS * (span / 360f)));
            Vector3 prev = origin + Quaternion.Euler(0, startAngle, 0) * forward * radius;
            for (int i = 1; i <= steps; i++)
            {
                float angle = startAngle + (i / (float)steps) * span;
                Vector3 next = origin + Quaternion.Euler(0, angle, 0) * forward * radius;
                UnityEngine.Debug.DrawLine(prev, next, color, duration);
                prev = next;
            }
        }
    }
}

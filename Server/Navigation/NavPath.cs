using System;

namespace KcpServer.Navigation
{
    public sealed class NavPath
    {
        public bool Success;
        public string FailReason = string.Empty;
        public Vector3[] Waypoints = Array.Empty<Vector3>();
        public float Length;

        public static NavPath Fail(string reason) => new NavPath { Success = false, FailReason = reason };

        public static NavPath Create(Vector3[] waypoints)
        {
            float length = 0f;
            for (int i = 1; i < waypoints.Length; i++)
            {
                float dx = waypoints[i].X - waypoints[i - 1].X;
                float dz = waypoints[i].Z - waypoints[i - 1].Z;
                length += MathF.Sqrt(dx * dx + dz * dz);
            }
            return new NavPath { Success = true, Waypoints = waypoints, Length = length };
        }
    }
}

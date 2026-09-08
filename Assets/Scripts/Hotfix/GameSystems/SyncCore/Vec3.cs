namespace Cool.SyncCore
{
    /// <summary>
    /// 最小 3D 向量（共享校验规则用）——SyncCore 零引擎依赖，
    /// 两端各自与 UnityEngine.Vector3 / KcpServer.Vector3 转换。
    /// </summary>
    public readonly struct Vec3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float DistanceSqr(Vec3 other)
        {
            float dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public float Distance(Vec3 other) => (float)System.Math.Sqrt(DistanceSqr(other));

        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public float Magnitude => (float)System.Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>归一化（零向量返回自身）</summary>
        public Vec3 Normalized()
        {
            float mag = Magnitude;
            return mag < 1e-6f ? this : new Vec3(X / mag, Y / mag, Z / mag);
        }
    }
}

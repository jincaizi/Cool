using System;

namespace KcpServer
{
    public struct Quaternion
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }

        public Quaternion(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        public static readonly Quaternion identity = new Quaternion(0, 0, 0, 1);

        public float this[int index] => index switch
        {
            0 => X, 1 => Y, 2 => Z, 3 => W,
            _ => throw new IndexOutOfRangeException()
        };

        public static Quaternion LookRotation(Vector3 forward)
        {
            float yaw = (float)Math.Atan2(forward.X, forward.Z);
            float cy = (float)Math.Cos(yaw * 0.5f);
            float sy = (float)Math.Sin(yaw * 0.5f);
            return new Quaternion(0, sy, 0, cy);
        }

        public Quaternion Normalize()
        {
            float mag = (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            if (mag < 0.0001f) return identity;
            return new Quaternion(X / mag, Y / mag, Z / mag, W / mag);
        }
    }
}

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
    }
}
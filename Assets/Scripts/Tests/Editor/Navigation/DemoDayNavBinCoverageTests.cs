using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.Navigation
{
    public class DemoDayNavBinCoverageTests
    {
        private static readonly (float X, float Z)[] Spawns =
        {
            (-30f, 0f), (0f, -20f), (0f, -35f), (10f, -15f), (10f, 15f), (25f, 20f)
        };

        [Test]
        public void Committed_NavBin_Contains_All_Fixed_Spawn_Points()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Server/Config/nav/demoday.navbin");
            Assert.IsTrue(File.Exists(path), "Export the DemoDay navmesh first.");

            (List<(float X, float Z)> verts, List<(int A, int B, int C)> tris) = ReadTriangles(path);
            Assert.Greater(tris.Count, 0);

            foreach (var spawn in Spawns)
            {
                bool inside = false;
                foreach (var tri in tris)
                {
                    var a = verts[tri.A]; var b = verts[tri.B]; var c = verts[tri.C];
                    if (PointInTriangle(spawn.X, spawn.Z, a, b, c)) { inside = true; break; }
                }
                if (!inside)
                {
                    // 允许投影到 0.5m 内的三角面
                    float best = float.MaxValue;
                    foreach (var tri in tris)
                    {
                        var a = verts[tri.A]; var b = verts[tri.B]; var c = verts[tri.C];
                        best = Math.Min(best, DistanceToTriangle(spawn.X, spawn.Z, a, b, c));
                    }
                    Assert.LessOrEqual(best, 0.5f, $"spawn ({spawn.X},{spawn.Z}) not on navmesh, nearest={best}");
                }
            }
        }

        private static (List<(float, float)>, List<(int, int, int)>) ReadTriangles(string path)
        {
            using var stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);
            r.ReadBytes(4); r.ReadUInt16();
            int mapLen = r.ReadUInt16(); r.ReadBytes(mapLen);
            r.ReadUInt64(); r.ReadUInt64();
            r.ReadSingle(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle();
            r.ReadSingle();
            int tileCount = r.ReadUInt16();
            int areaCount = r.ReadByte();
            for (int i = 0; i < areaCount; i++) r.ReadSingle();
            int vertexCount = (int)r.ReadUInt32();
            var verts = new List<(float, float)>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                float x = r.ReadSingle(); float y = r.ReadSingle(); float z = r.ReadSingle();
                verts.Add((x, z));
            }
            var tris = new List<(int, int, int)>();
            for (int t = 0; t < tileCount; t++)
            {
                r.ReadInt16(); r.ReadInt16();
                int triCount = (int)r.ReadUInt32();
                for (int i = 0; i < triCount; i++)
                {
                    int a = (int)r.ReadUInt32(); int b = (int)r.ReadUInt32(); int c = (int)r.ReadUInt32();
                    tris.Add((a, b, c));
                }
                r.ReadBytes(triCount);
            }
            return (verts, tris);
        }

        private static bool PointInTriangle(float px, float pz, (float X, float Z) a,
            (float X, float Z) b, (float X, float Z) c)
        {
            float d1 = Sign(px, pz, a, b);
            float d2 = Sign(px, pz, b, c);
            float d3 = Sign(px, pz, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private static float Sign(float px, float pz, (float X, float Z) a, (float X, float Z) b)
            => (px - b.X) * (a.Z - b.Z) - (a.X - b.X) * (pz - b.Z);

        private static float DistanceToTriangle(float px, float pz, (float X, float Z) a,
            (float X, float Z) b, (float X, float Z) c)
            => Math.Min(DistanceToSegment(px, pz, a, b),
                Math.Min(DistanceToSegment(px, pz, b, c), DistanceToSegment(px, pz, c, a)));

        private static float DistanceToSegment(float px, float pz, (float X, float Z) a, (float X, float Z) b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq <= 0.000001f ? 0f : ((px - a.X) * dx + (pz - a.Z) * dz) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float x = a.X + dx * t, z = a.Z + dz * t;
            float ex = px - x, ez = pz - z;
            return MathF.Sqrt(ex * ex + ez * ez);
        }
    }
}

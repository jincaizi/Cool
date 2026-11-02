using System;
using System.Collections.Generic;
using KcpServer;
using KcpServer.Navigation;

namespace KcpServer.Tests.Navigation;

internal static class NavMeshFixture
{
    /// <summary>生成 1m 网格；walkable(x,z) 返回 false 的格子被挖空。顶点按坐标去重。</summary>
    public static NavMeshGraph Grid(int width, int depth, Func<int, int, bool> walkable)
    {
        var vertices = new List<Vector3>();
        var lookup = new Dictionary<(int, int), int>();

        int Vertex(float x, float z)
        {
            int qx = (int)MathF.Round(x * 1000f);
            int qz = (int)MathF.Round(z * 1000f);
            if (lookup.TryGetValue((qx, qz), out int index)) return index;
            index = vertices.Count;
            vertices.Add(new Vector3(x, 0f, z));
            lookup[(qx, qz)] = index;
            return index;
        }

        var indices = new List<int>();
        var areas = new List<byte>();

        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
        {
            if (!walkable(x, z)) continue;
            int a = Vertex(x, z);
            int b = Vertex(x + 1, z);
            int c = Vertex(x + 1, z + 1);
            int d = Vertex(x, z + 1);
            indices.AddRange(new[] { a, b, c }); areas.Add(0);
            indices.AddRange(new[] { a, c, d }); areas.Add(0);
        }

        return NavMeshGraph.FromTriangles(vertices.ToArray(), indices.ToArray(), areas.ToArray(), new[] { 1f, 1f, 2f });
    }
}

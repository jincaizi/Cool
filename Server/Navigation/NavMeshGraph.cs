using System;
using System.Collections.Generic;

namespace KcpServer.Navigation
{
    public sealed class NavMeshGraph
    {
        private const float ClearanceCap = 8f;
        private const float CellSize = 4f;

        private readonly Vector3[] _vertices;
        private readonly int[] _indices;
        private readonly byte[] _areas;
        private readonly float[] _areaCosts;
        private readonly Triangle[] _triangles;
        private readonly int[][] _neighbors;
        private readonly List<(Vector3 A, Vector3 B)> _boundaryEdges;
        private readonly Dictionary<long, List<int>> _triangleGrid;
        private readonly Dictionary<long, List<int>> _boundaryGrid;

        public int TriangleCount => _triangles.Length;
        public int VertexCount => _vertices.Length;
        public int BoundaryEdgeCount => _boundaryEdges.Count;
        public float MinX { get; }
        public float MinZ { get; }
        public float MaxX { get; }
        public float MaxZ { get; }

        private readonly struct Triangle
        {
            public readonly int A, B, C;
            public readonly float CentroidX, CentroidZ;
            public readonly float MinX, MinZ, MaxX, MaxZ;
            public readonly float Clearance;
            public Triangle(int a, int b, int c, float cx, float cz, float minX, float minZ,
                float maxX, float maxZ, float clearance)
            {
                A = a; B = b; C = c; CentroidX = cx; CentroidZ = cz;
                MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; Clearance = clearance;
            }
        }

        public static NavMeshGraph FromData(NavData data)
        {
            var allIndices = new List<int>();
            var allAreas = new List<byte>();
            foreach (NavTileData tile in data.Tiles)
            {
                allIndices.AddRange(tile.Indices);
                allAreas.AddRange(tile.Areas);
            }
            return new NavMeshGraph(data.Vertices, allIndices.ToArray(), allAreas.ToArray(), data.AreaCosts);
        }

        public static NavMeshGraph FromTriangles(Vector3[] vertices, int[] indices, byte[] areas, float[] areaCosts)
            => new NavMeshGraph(vertices, indices, areas, areaCosts);

        private NavMeshGraph(Vector3[] vertices, int[] indices, byte[] areas, float[] areaCosts)
        {
            if (vertices == null || vertices.Length == 0) throw new ArgumentException("no vertices", nameof(vertices));
            if (indices == null || indices.Length == 0 || indices.Length % 3 != 0)
                throw new ArgumentException("indices must be non-empty multiple of 3", nameof(indices));
            if (areas == null || areas.Length != indices.Length / 3)
                throw new ArgumentException("areas mismatch", nameof(areas));

            // 图拥有自己的数组：winding 归一化会原地改写索引，
            // 且构造中途抛异常时不应把部分改写留给调用方。
            _vertices = (Vector3[])vertices.Clone();
            _indices = (int[])indices.Clone();
            _areas = (byte[])areas.Clone();
            _areaCosts = areaCosts == null ? new[] { 1f } : (float[])areaCosts.Clone();

            int triangleCount = indices.Length / 3;
            var triangles = new Triangle[triangleCount];
            _boundaryEdges = new List<(Vector3, Vector3)>();
            _triangleGrid = new Dictionary<long, List<int>>();
            _boundaryGrid = new Dictionary<long, List<int>>();

            var edgeMap = new Dictionary<long, (int A, int B, int T0, int T1)>();
            var neighborLists = new List<int>[triangleCount];

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < triangleCount; i++)
            {
                int a = _indices[i * 3], b = _indices[i * 3 + 1], c = _indices[i * 3 + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                    throw new ArgumentException("index out of range");

                Vector3 va = vertices[a], vb = vertices[b], vc = vertices[c];
                if (SignedArea2(va, vb, vc) < 0f)
                {
                    (b, c) = (c, b);
                    vb = vertices[b]; vc = vertices[c];
                    _indices[i * 3 + 1] = b;
                    _indices[i * 3 + 2] = c;
                }

                float triMinX = MathF.Min(va.X, MathF.Min(vb.X, vc.X));
                float triMinZ = MathF.Min(va.Z, MathF.Min(vb.Z, vc.Z));
                float triMaxX = MathF.Max(va.X, MathF.Max(vb.X, vc.X));
                float triMaxZ = MathF.Max(va.Z, MathF.Max(vb.Z, vc.Z));
                float cx = (va.X + vb.X + vc.X) / 3f;
                float cz = (va.Z + vb.Z + vc.Z) / 3f;
                triangles[i] = new Triangle(a, b, c, cx, cz, triMinX, triMinZ, triMaxX, triMaxZ, ClearanceCap);
                neighborLists[i] = new List<int>(3);

                AddEdge(edgeMap, a, b, i);
                AddEdge(edgeMap, b, c, i);
                AddEdge(edgeMap, c, a, i);

                InsertAabb(_triangleGrid, triMinX, triMinZ, triMaxX, triMaxZ, i);

                minX = MathF.Min(minX, triMinX); minZ = MathF.Min(minZ, triMinZ);
                maxX = MathF.Max(maxX, triMaxX); maxZ = MathF.Max(maxZ, triMaxZ);
            }

            foreach (var kv in edgeMap)
            {
                var (a, b, t0, t1) = kv.Value;
                if (t1 < 0)
                {
                    _boundaryEdges.Add((vertices[a], vertices[b]));
                    continue;
                }
                neighborLists[t0].Add(t1);
                neighborLists[t1].Add(t0);
            }

            for (int i = 0; i < triangleCount; i++)
                neighborLists[i].Sort();

            _neighbors = new int[triangleCount][];
            for (int i = 0; i < triangleCount; i++) _neighbors[i] = neighborLists[i].ToArray();

            for (int i = 0; i < _boundaryEdges.Count; i++)
            {
                var (a, b) = _boundaryEdges[i];
                InsertAabb(_boundaryGrid, MathF.Min(a.X, b.X), MathF.Min(a.Z, b.Z),
                    MathF.Max(a.X, b.X), MathF.Max(a.Z, b.Z), i);
            }

            for (int i = 0; i < triangleCount; i++)
            {
                Triangle t = triangles[i];
                float clearance = MathF.Min(
                    MathF.Min(ClearanceAt(t.CentroidX, t.CentroidZ),
                        ClearanceAt((_vertices[t.A].X + _vertices[t.B].X) * 0.5f, (_vertices[t.A].Z + _vertices[t.B].Z) * 0.5f)),
                    MathF.Min(ClearanceAt((_vertices[t.B].X + _vertices[t.C].X) * 0.5f, (_vertices[t.B].Z + _vertices[t.C].Z) * 0.5f),
                        ClearanceAt((_vertices[t.C].X + _vertices[t.A].X) * 0.5f, (_vertices[t.C].Z + _vertices[t.A].Z) * 0.5f)));
                triangles[i] = new Triangle(t.A, t.B, t.C, t.CentroidX, t.CentroidZ, t.MinX, t.MinZ, t.MaxX, t.MaxZ, clearance);
            }

            _triangles = triangles;
            MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ;
        }

        public int[] CopyIndices() => (int[])_indices.Clone();
        public byte[] CopyAreas() => (byte[])_areas.Clone();
        public Vector3[] CopyVertices() => (Vector3[])_vertices.Clone();
        public float GetClearance(int triangle) => _triangles[triangle].Clearance;
        public int GetTriangleCount() => _triangles.Length;
        public int GetNeighborCount(int triangle) => _neighbors[triangle].Length;
        public int GetNeighbor(int triangle, int index) => _neighbors[triangle][index];
        public float AreaCost(int triangle)
        {
            byte area = _areas[triangle];
            return area < _areaCosts.Length ? _areaCosts[area] : 1f;
        }

        public void GetTriangleCentroid(int triangle, out float x, out float z)
        {
            x = _triangles[triangle].CentroidX;
            z = _triangles[triangle].CentroidZ;
        }

        public bool FindNearestTriangle(Vector3 point, float maxDistance, out int triangle, out float distance)
        {
            triangle = -1;
            distance = float.PositiveInfinity;
            int cellX = CellOf(point.X), cellZ = CellOf(point.Z);
            if (ContainsInCell(cellX, cellZ, point, out triangle))
            {
                distance = 0f;
                return true;
            }

            int radiusCells = Math.Max(1, (int)MathF.Ceiling(maxDistance / CellSize));
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                long key = CellKey(cellX + dx, cellZ + dz);
                if (!_triangleGrid.TryGetValue(key, out List<int>? candidates)) continue;
                foreach (int candidate in candidates)
                {
                    float d = DistanceToTriangle(point, candidate);
                    if (d < distance)
                    {
                        distance = d;
                        triangle = candidate;
                    }
                }
            }

            if (triangle >= 0 && distance <= maxDistance) return true;
            triangle = -1;
            distance = float.PositiveInfinity;
            return false;
        }

        public bool FindNearestPoint(Vector3 point, float maxDistance, out Vector3 projected,
            out float distance, out int triangle)
        {
            projected = point;
            distance = float.PositiveInfinity;
            triangle = -1;
            if (!FindNearestTriangle(point, maxDistance, out int candidate, out _)) return false;

            triangle = candidate;
            if (PointInTriangle(point, candidate))
            {
                projected = point;
                distance = 0f;
                return true;
            }

            projected = ClosestPointOnTriangleXZ(point, candidate);
            distance = DistanceXZ(point, projected);
            return distance <= maxDistance;
        }

        private Vector3 ClosestPointOnTriangleXZ(Vector3 p, int triangle)
        {
            Triangle t = _triangles[triangle];
            Vector3 a = _vertices[t.A], b = _vertices[t.B], c = _vertices[t.C];
            Vector3 ab = ClosestPointOnSegment(p, a, b);
            Vector3 bc = ClosestPointOnSegment(p, b, c);
            Vector3 ca = ClosestPointOnSegment(p, c, a);
            float dab = DistanceXZ(p, ab), dbc = DistanceXZ(p, bc), dca = DistanceXZ(p, ca);
            if (dab <= dbc && dab <= dca) return ab;
            if (dbc <= dca) return bc;
            return ca;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq <= 0.000001f ? 0f : ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            return new Vector3(a.X + dx * t, p.Y, a.Z + dz * t);
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to, float bodyRadius)
        {
            float dx = to.X - from.X, dz = to.Z - from.Z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / 0.25f));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                float x = from.X + dx * t;
                float z = from.Z + dz * t;
                if (!IsPointNavigable(new Vector3(x, from.Y, z), bodyRadius)) return false;
            }
            return true;
        }

        public bool IsPointNavigable(Vector3 point, float bodyRadius)
        {
            if (!FindNearestTriangle(point, 0.25f, out int triangle, out float distance)) return false;
            return distance <= 0.25f && ClearanceAt(point.X, point.Z) >= bodyRadius;
        }

        public bool GetPortal(int fromTriangle, int toTriangle, out Vector3 left, out Vector3 right)
        {
            left = default; right = default;
            Triangle a = _triangles[fromTriangle];
            Triangle b = _triangles[toTriangle];
            int shared0 = -1, shared1 = -1;
            if (!TryShared(a, b, ref shared0, ref shared1)) return false;
            if (shared0 < 0 || shared1 < 0) return false;

            Vector3 p0 = _vertices[shared0];
            Vector3 p1 = _vertices[shared1];
            float dx = b.CentroidX - a.CentroidX;
            float dz = b.CentroidZ - a.CentroidZ;
            float cross = dx * (p0.Z - a.CentroidZ) - dz * (p0.X - a.CentroidX);
            if (cross > 0f) { left = p0; right = p1; }
            else { left = p1; right = p0; }
            return true;
        }

        private bool ContainsInCell(int cellX, int cellZ, Vector3 point, out int triangle)
        {
            triangle = -1;
            if (!_triangleGrid.TryGetValue(CellKey(cellX, cellZ), out List<int>? candidates)) return false;
            foreach (int candidate in candidates)
            {
                if (PointInTriangle(point, candidate))
                {
                    triangle = candidate;
                    return true;
                }
            }
            return false;
        }

        private bool PointInTriangle(Vector3 p, int triangle)
        {
            Triangle t = _triangles[triangle];
            Vector3 a = _vertices[t.A], b = _vertices[t.B], c = _vertices[t.C];
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private float DistanceToTriangle(Vector3 p, int triangle)
        {
            Triangle t = _triangles[triangle];
            Vector3 a = _vertices[t.A], b = _vertices[t.B], c = _vertices[t.C];
            float d0 = DistancePointSegment(p, a, b);
            float d1 = DistancePointSegment(p, b, c);
            float d2 = DistancePointSegment(p, c, a);
            return MathF.Min(d0, MathF.Min(d1, d2));
        }

        private float ClearanceAt(float x, float z)
        {
            int cellX = CellOf(x), cellZ = CellOf(z);
            int radius = (int)MathF.Ceiling(ClearanceCap / CellSize);
            float best = ClearanceCap;
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (!_boundaryGrid.TryGetValue(CellKey(cellX + dx, cellZ + dz), out List<int>? edges)) continue;
                foreach (int edgeIndex in edges)
                {
                    var (a, b) = _boundaryEdges[edgeIndex];
                    float d = DistancePointSegmentXZ(x, z, a, b);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        private static void AddEdge(Dictionary<long, (int A, int B, int T0, int T1)> map, int a, int b, int triangle)
        {
            int min = Math.Min(a, b), max = Math.Max(a, b);
            long key = ((long)min << 32) | (uint)max;
            if (map.TryGetValue(key, out var record))
            {
                if (record.T1 >= 0) throw new InvalidOperationException("non-manifold edge");
                map[key] = (record.A, record.B, record.T0, triangle);
            }
            else
            {
                map[key] = (min, max, triangle, -1);
            }
        }

        private static bool TryShared(Triangle a, Triangle b, ref int shared0, ref int shared1)
        {
            Span<int> av = stackalloc[] { a.A, a.B, a.C };
            Span<int> bv = stackalloc[] { b.A, b.B, b.C };
            int found = 0;
            foreach (int va in av)
            foreach (int vb in bv)
            {
                if (va != vb) continue;
                if (found == 0) shared0 = va;
                else if (found == 1) shared1 = va;
                found++;
            }
            return found == 2;
        }

        private static float SignedArea2(Vector3 a, Vector3 b, Vector3 c)
            => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

        private static float Sign(Vector3 p, Vector3 a, Vector3 b)
            => (p.X - b.X) * (a.Z - b.Z) - (a.X - b.X) * (p.Z - b.Z);

        private static float DistancePointSegment(Vector3 p, Vector3 a, Vector3 b)
            => DistancePointSegmentXZ(p.X, p.Z, a, b);

        private static float DistancePointSegmentXZ(float px, float pz, Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq <= 0.000001f ? 0f : ((px - a.X) * dx + (pz - a.Z) * dz) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float x = a.X + dx * t, z = a.Z + dz * t;
            float ex = px - x, ez = pz - z;
            return MathF.Sqrt(ex * ex + ez * ez);
        }

        private static void InsertAabb(Dictionary<long, List<int>> grid, float minX, float minZ, float maxX, float maxZ, int value)
        {
            int x0 = CellOf(minX), x1 = CellOf(maxX), z0 = CellOf(minZ), z1 = CellOf(maxZ);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                long key = CellKey(x, z);
                if (!grid.TryGetValue(key, out List<int>? list))
                {
                    list = new List<int>();
                    grid[key] = list;
                }
                list.Add(value);
            }
        }

        private static int CellOf(float value) => (int)MathF.Floor(value / CellSize);
        private static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}

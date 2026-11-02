using System;
using System.Collections.Generic;
using System.Linq;

namespace KcpServer.Navigation
{
    public sealed class NavMeshPathfinder
    {
        private const float DefaultProjectionRadius = 2f;
        private const float ValidationStep = 0.5f;
        private const float ClearanceHardFactor = 0.9f;
        private const float ClearanceSoftFactor = 1.1f;
        private const float ClearancePenalty = 2f;
        private const float StartProbeDistance = 0.05f;

        private readonly NavMeshGraph _graph;

        public NavMeshPathfinder(NavMeshGraph graph) => _graph = graph ?? throw new ArgumentNullException(nameof(graph));

        public NavPath FindPath(Vector3 start, Vector3 goal, float bodyRadius, int maxExpandedNodes = 50000)
            => FindPathInternal(start, goal, bodyRadius, maxExpandedNodes, 0);

        private NavPath FindPathInternal(Vector3 start, Vector3 goal, float bodyRadius, int maxExpandedNodes, int attempt)
        {
            if (!_graph.FindNearestPoint(start, DefaultProjectionRadius, out Vector3 projectedStart, out _, out int startTri))
                return NavPath.Fail("start-off-navmesh");
            if (!_graph.FindNearestPoint(goal, DefaultProjectionRadius, out Vector3 projectedGoal, out _, out int goalTri))
                return NavPath.Fail("goal-off-navmesh");

            // 起点可能正好落在三角面顶点/边上；此时任选一个邻接三角面都可能朝后，
            // 会导致漏斗从背后起头。朝目标方向探一点，优先选前方三角面。
            float toGoalX = projectedGoal.X - projectedStart.X;
            float toGoalZ = projectedGoal.Z - projectedStart.Z;
            float toGoalLength = MathF.Sqrt(toGoalX * toGoalX + toGoalZ * toGoalZ);
            if (toGoalLength > 0.001f)
            {
                var probe = new Vector3(
                    projectedStart.X + toGoalX / toGoalLength * StartProbeDistance,
                    projectedStart.Y,
                    projectedStart.Z + toGoalZ / toGoalLength * StartProbeDistance);
                if (_graph.FindNearestPoint(probe, 1f, out _, out _, out int probeTri))
                    startTri = probeTri;
            }

            if (startTri == goalTri)
            {
                var direct = new[] { projectedStart, projectedGoal };
                if (IsValidSegment(projectedStart, projectedGoal, bodyRadius)) return NavPath.Create(direct);
            }

            int n = _graph.TriangleCount;
            var gScore = new float[n];
            var cameFrom = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { gScore[i] = float.PositiveInfinity; cameFrom[i] = -1; }

            var open = new PriorityQueue<int, (float F, int Index)>();
            gScore[startTri] = 0f;
            open.Enqueue(startTri, (Heuristic(startTri, goalTri), startTri));

            int expanded = 0;
            while (open.TryDequeue(out int current, out _))
            {
                if (closed[current]) continue;
                closed[current] = true;
                if (current == goalTri) break;
                if (++expanded > maxExpandedNodes) return NavPath.Fail("too-complex");

                int count = _graph.GetNeighborCount(current);
                for (int k = 0; k < count; k++)
                {
                    int next = _graph.GetNeighbor(current, k);
                    if (closed[next]) continue;
                    // 三角面 clearance 取质心/边中点最小值，贴墙三角面恒为 0，
                    // 按面做硬过滤会封死合法通道；改为按两三角面之间的门（portal）中点判定。
                    bool exempt = next == goalTri || current == startTri;
                    if (!exempt && !PortalFitsBody(current, next, bodyRadius * ClearanceHardFactor)) continue;

                    float step = DistanceXZ(current, next) * _graph.AreaCost(next);
                    step += ClearancePenalty * MathF.Max(0f, bodyRadius * ClearanceSoftFactor - _graph.GetClearance(next));
                    float tentative = gScore[current] + step;
                    if (tentative + 1e-5f >= gScore[next]) continue;

                    gScore[next] = tentative;
                    cameFrom[next] = current;
                    open.Enqueue(next, (tentative + Heuristic(next, goalTri), next));
                }
            }

            if (cameFrom[goalTri] < 0 && startTri != goalTri) return NavPath.Fail("blocked");

            var triPath = new List<int>();
            for (int tri = goalTri; tri >= 0; tri = cameFrom[tri])
            {
                triPath.Add(tri);
                if (tri == startTri) break;
            }
            triPath.Reverse();
            if (triPath.Count == 0 || triPath[0] != startTri) return NavPath.Fail("blocked");

            var points = new List<Vector3>(triPath.Count * 2 + 2) { projectedStart };
            for (int i = 0; i + 1 < triPath.Count; i++)
            {
                if (!_graph.GetPortal(triPath[i], triPath[i + 1], out Vector3 left, out Vector3 right))
                    return NavPath.Fail("portal-missing");
                // GetPortal 的 left/right 与漏斗 TriArea2 的左右约定相反：
                // 按 GetPortal 顺序直接喂入会让漏斗贴着阶梯外侧走。这里交换。
                // 同时按 bodyRadius 收缩门，保证漏斗路径与墙保持身体半径。
                points.Add(ShrinkPortalEndpoint(right, left, bodyRadius));
                points.Add(ShrinkPortalEndpoint(left, right, bodyRadius));
            }
            points.Add(projectedGoal);

            List<Vector3> waypoints = Funnel(points);
            if (!ValidatePath(waypoints, bodyRadius))
            {
                if (attempt == 0)
                    return FindPathInternal(start, goal, bodyRadius * 1.15f, maxExpandedNodes, 1);
                return NavPath.Fail("blocked");
            }
            return NavPath.Create(waypoints.ToArray());
        }

        public bool HasLineOfSight(Vector3 a, Vector3 b, float bodyRadius)
            => _graph.HasLineOfSight(a, b, bodyRadius);

        private static Vector3 ShrinkPortalEndpoint(Vector3 point, Vector3 other, float bodyRadius)
        {
            float dx = other.X - point.X, dz = other.Z - point.Z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            if (length <= 1e-5f) return point;
            float amount = MathF.Min(bodyRadius, length * 0.5f);
            return new Vector3(point.X + dx / length * amount, point.Y, point.Z + dz / length * amount);
        }

        private bool PortalFitsBody(int fromTriangle, int toTriangle, float clearance)
        {
            if (!_graph.GetPortal(fromTriangle, toTriangle, out Vector3 left, out Vector3 right))
                return true;
            var midpoint = new Vector3((left.X + right.X) * 0.5f, left.Y, (left.Z + right.Z) * 0.5f);
            return _graph.IsPointNavigable(midpoint, clearance);
        }

        private bool ValidatePath(List<Vector3> points, float bodyRadius)
        {
            for (int i = 1; i < points.Count; i++)
                if (!IsValidSegment(points[i - 1], points[i], bodyRadius)) return false;
            return true;
        }

        private bool IsValidSegment(Vector3 a, Vector3 b, float bodyRadius)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / ValidationStep));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                // 起终点 1m 内放宽：怪可能贴着墙/岩石出生，甚至投影点就在网格边界上
                float along = t * length;
                float radius = (along < 1f || length - along < 1f) ? 0f : bodyRadius;
                var p = new Vector3(a.X + dx * t, a.Y, a.Z + dz * t);
                if (!_graph.IsPointNavigable(p, radius)) return false;
            }
            return true;
        }

        private float Heuristic(int from, int goal)
        {
            _graph.GetTriangleCentroid(from, out float fx, out float fz);
            _graph.GetTriangleCentroid(goal, out float gx, out float gz);
            float dx = gx - fx, dz = gz - fz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private float DistanceXZ(int a, int b)
        {
            _graph.GetTriangleCentroid(a, out float ax, out float az);
            _graph.GetTriangleCentroid(b, out float bx, out float bz);
            float dx = bx - ax, dz = bz - az;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static List<Vector3> Funnel(List<Vector3> points)
        {
            // points = [start, L0,R0, L1,R1, ..., goal]
            var result = new List<Vector3>();
            if (points.Count < 2) return result;

            Vector3 apex = points[0];
            Vector3 left = points[0];
            Vector3 right = points[0];
            int apexIndex = 0, leftIndex = 0, rightIndex = 0;
            result.Add(apex);

            int portalCount = (points.Count - 2) / 2;
            for (int i = 0; i < portalCount; i++)
            {
                Vector3 newLeft = points[1 + i * 2];
                Vector3 newRight = points[2 + i * 2];

                if (TriArea2(apex, right, newRight) <= 0f)
                {
                    if (apex.Equals(right) || TriArea2(apex, left, newRight) > 0f)
                    {
                        right = newRight;
                        rightIndex = i;
                    }
                    else
                    {
                        result.Add(left);
                        apex = left;
                        apexIndex = leftIndex;
                        left = apex; right = apex;
                        leftIndex = apexIndex; rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }

                if (TriArea2(apex, left, newLeft) >= 0f)
                {
                    if (apex.Equals(left) || TriArea2(apex, right, newLeft) < 0f)
                    {
                        left = newLeft;
                        leftIndex = i;
                    }
                    else
                    {
                        result.Add(right);
                        apex = right;
                        apexIndex = rightIndex;
                        left = apex; right = apex;
                        leftIndex = apexIndex; rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }
            }

            result.Add(points[^1]);
            return result;
        }

        private static float TriArea2(Vector3 a, Vector3 b, Vector3 c)
            => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace KcpServer.Navigation
{
    public sealed class NavMeshPathfinder
    {
        private const float DefaultProjectionRadius = 2f;
        // 0.1m → 0.25m（C1 性能修复）：路径校验每 0.1m 一次 IsPointNavigable（每次含
        // FindNearestTriangle + 25 格边界边扫描），是单次查询的主要开销。0.25m 仍小于最小
        // bodyRadius（0.5m）的一半，窄通道/墙角用例（NavMeshPathfinderTests）全部保留。
        private const float ValidationStep = 0.25f;
        private const float ClearanceHardFactor = 0.9f;
        private const float ClearanceSoftFactor = 1.1f;
        private const float ClearancePenalty = 2f;
        private const float StartProbeDistance = 0.05f;
        private const float EndpointEpsilon = 1e-4f;

        private readonly NavMeshGraph _graph;
        // portal clearance 与 bodyRadius 无关的部分是静态的：同一对三角面的门在整张地图生命周期内不变。
        // 缓存 (门端点, 量化后的 maxClearance) → (max clearance 点, 值)，跨查询复用（200 怪反复寻路）。
        private readonly Dictionary<(float, float, float, float, int), (float Clearance, Vector3 BestPoint)> _portalCache = new();
        private const int PortalCacheLimit = 65_536;

        // 单次查询的软截止时间（绝对 Stopwatch 时间戳；0 = 不限）。NavigationService 传入
        // 本 tick 剩余预算，避免一条跨长距离/Boss 大半径查询把 tick 拖过 66ms。
        // 非线程安全：NavMeshPathfinder 是每个 NavigationService 私有的，主循环单线程（R2）。
        private long _deadlineTimestamp;
        private bool _budgetExceeded;

        public NavMeshPathfinder(NavMeshGraph graph) => _graph = graph ?? throw new ArgumentNullException(nameof(graph));

        public NavPath FindPath(Vector3 start, Vector3 goal, float bodyRadius, int maxExpandedNodes = 50000)
        {
            _budgetExceeded = false; // 上一次带 deadline 的查询可能留下 true
            return FindPathInternal(start, goal, bodyRadius, maxExpandedNodes, 0);
        }

        /// <summary>带单次查询时间上限的重载（毫秒）。超限返回 <c>FailReason="budget"</c>，
        /// 由调用方下 tick 重试；已完成的 portal clearance 仍在缓存里，重试通常更快。</summary>
        public NavPath FindPath(Vector3 start, Vector3 goal, float bodyRadius, int maxExpandedNodes, double maxMilliseconds)
        {
            if (maxMilliseconds >= double.MaxValue)
                return FindPathInternal(start, goal, bodyRadius, maxExpandedNodes, 0);
            if (maxMilliseconds <= 0) return NavPath.Fail("budget");

            long previous = _deadlineTimestamp;
            _deadlineTimestamp = Stopwatch.GetTimestamp()
                + (long)(maxMilliseconds / 1000.0 * Stopwatch.Frequency);
            _budgetExceeded = false;
            try
            {
                return FindPathInternal(start, goal, bodyRadius, maxExpandedNodes, 0);
            }
            finally
            {
                _deadlineTimestamp = previous;
            }
        }

        private bool BudgetExceeded()
        {
            if (_deadlineTimestamp == 0 || Stopwatch.GetTimestamp() < _deadlineTimestamp) return false;
            _budgetExceeded = true;
            return true;
        }

        private NavPath FindPathInternal(Vector3 start, Vector3 goal, float bodyRadius, int maxExpandedNodes, int attempt)
        {
            if (BudgetExceeded()) return NavPath.Fail("budget");
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

            // 不同连通分量：A* 无论怎么展开都不通，直接 blocked（跨河/孤岛查询的主要开销在这里）。
            if (_graph.GetComponent(startTri) != _graph.GetComponent(goalTri))
                return NavPath.Fail("blocked");

            if (startTri == goalTri)
            {
                var direct = new[] { projectedStart, projectedGoal };
                if (IsValidSegment(projectedStart, projectedGoal, bodyRadius, projectedStart, projectedGoal))
                    return NavPath.Create(direct);
            }

            int n = _graph.TriangleCount;
            var gScore = new float[n];
            var cameFrom = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { gScore[i] = float.PositiveInfinity; cameFrom[i] = -1; }

            var open = new PriorityQueue<int, (float F, int Index)>();
            bool sameTriangle = startTri == goalTri;
            if (sameTriangle)
            {
                // 直连校验失败但起终点在同一三角面：不要立刻 blocked。从 start 的邻居继续扩展，
                // 允许路径绕开贴近边界的采样点再回到目标（Minor：startTri==goalTri 扩展）。
                int neighborCount = _graph.GetNeighborCount(startTri);
                for (int k = 0; k < neighborCount; k++)
                {
                    int next = _graph.GetNeighbor(startTri, k);
                    float portalClearance = PortalClearance(startTri, next, bodyRadius * ClearanceSoftFactor, out _);
                    float step = DistanceXZ(startTri, next) * _graph.AreaCost(next);
                    step += ClearancePenalty * MathF.Max(0f, bodyRadius * ClearanceSoftFactor - portalClearance);
                    gScore[next] = step;
                    cameFrom[next] = startTri;
                    open.Enqueue(next, (step + Heuristic(next, goalTri), next));
                }
            }
            else
            {
                gScore[startTri] = 0f;
                open.Enqueue(startTri, (Heuristic(startTri, goalTri), startTri));
            }

            int expanded = 0;
            while (open.TryDequeue(out int current, out _))
            {
                if (closed[current]) continue;
                closed[current] = true;
                if (current == goalTri) break;
                if (++expanded > maxExpandedNodes) return NavPath.Fail("too-complex");
                if (BudgetExceeded()) return NavPath.Fail("budget");

                int count = _graph.GetNeighborCount(current);
                for (int k = 0; k < count; k++)
                {
                    int next = _graph.GetNeighbor(current, k);
                    if (closed[next]) continue;
                    // 三角面 clearance 取质心/边中点最小值，贴墙三角面恒为 0，
                    // 按面做硬过滤会封死合法通道。改为对门采样，取门内最大可通行 clearance。
                    float portalClearance = PortalClearance(current, next, bodyRadius * ClearanceSoftFactor, out _);
                    bool exempt = next == goalTri || current == startTri;
                    if (!exempt && portalClearance < bodyRadius * ClearanceHardFactor) continue;

                    float step = DistanceXZ(current, next) * _graph.AreaCost(next);
                    step += ClearancePenalty * MathF.Max(0f, bodyRadius * ClearanceSoftFactor - portalClearance);
                    float tentative = gScore[current] + step;
                    if (tentative + 1e-5f >= gScore[next]) continue;

                    gScore[next] = tentative;
                    cameFrom[next] = current;
                    open.Enqueue(next, (tentative + Heuristic(next, goalTri), next));
                }
            }

            if (cameFrom[goalTri] < 0 && startTri != goalTri) return NavPath.Fail("blocked");

            var triPath = new List<int>();
            for (int tri = goalTri; tri >= 0 && triPath.Count <= n; tri = cameFrom[tri])
            {
                triPath.Add(tri);
                // goalTri == startTri 时第一项就相等——必须至少走一步 cameFrom，
                // 否则“从邻居绕回来”的路径会被截成单三角面（Minor 修复）。
                if (tri == startTri && triPath.Count > 1) break;
            }
            triPath.Reverse();
            if (triPath.Count == 0 || triPath[0] != startTri) return NavPath.Fail("blocked");

            var points = new List<Vector3>(triPath.Count * 2 + 2) { projectedStart };
            var rawPortals = new List<(Vector3 Left, Vector3 Right)>(triPath.Count);
            for (int i = 0; i + 1 < triPath.Count; i++)
            {
                if (!_graph.GetPortal(triPath[i], triPath[i + 1], out Vector3 left, out Vector3 right))
                    return NavPath.Fail("portal-missing");
                rawPortals.Add((left, right));
                // GetPortal 的 left/right 与漏斗 TriArea2 的左右约定相反：
                // 按 GetPortal 顺序直接喂入会让漏斗贴着阶梯外侧走。这里交换。
                // 两端各向门内 clearance 最大的点收缩到 bodyRadius 可通行处；
                // 斜门沿门边收缩会低估法向距离，用 IsPointNavigable 采样兜底。
                PortalClearance(triPath[i], triPath[i + 1], bodyRadius, out Vector3 portalBestPoint);
                points.Add(ShrinkPortalEndpointTo(right, portalBestPoint, bodyRadius));
                points.Add(ShrinkPortalEndpointTo(left, portalBestPoint, bodyRadius));
            }
            points.Add(projectedGoal);

            List<Vector3> waypoints = Funnel(points);
            if (!ValidatePath(waypoints, bodyRadius))
            {
                if (_budgetExceeded) return NavPath.Fail("budget");
                // 漏斗在凸墙角会切角；退化为“门内最大 clearance 点/墙角偏移点 + 可见性捷径”重排。
                List<Vector3> candidates = BuildClearanceCandidates(rawPortals, projectedStart, projectedGoal, bodyRadius);
                waypoints = ShortcutPath(candidates, bodyRadius);
            }
            if (!ValidatePath(waypoints, bodyRadius))
            {
                if (_budgetExceeded) return NavPath.Fail("budget");
                if (attempt == 0)
                    return FindPathInternal(start, goal, bodyRadius * 1.15f, maxExpandedNodes, 1);
                return NavPath.Fail("blocked");
            }
            return NavPath.Create(waypoints.ToArray());
        }

        public bool HasLineOfSight(Vector3 a, Vector3 b, float bodyRadius)
            => _graph.HasLineOfSight(a, b, bodyRadius);

        private Vector3 ShrinkPortalEndpointTo(Vector3 from, Vector3 target, float bodyRadius)
        {
            float dx = target.X - from.X, dz = target.Z - from.Z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            if (length <= 1e-5f) return from;
            float step = MathF.Max(0.05f, bodyRadius * 0.25f);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / step));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var p = new Vector3(from.X + dx * t, from.Y, from.Z + dz * t);
                if (_graph.IsPointNavigable(p, bodyRadius)) return p;
            }
            return target;
        }

        private float PortalClearance(int fromTriangle, int toTriangle, float maxClearance, out Vector3 bestPoint)
        {
            bestPoint = default;
            if (!_graph.GetPortal(fromTriangle, toTriangle, out Vector3 left, out Vector3 right)) return 0f;
            return PortalClearance(left, right, maxClearance, out bestPoint);
        }

        private float PortalClearance(Vector3 left, Vector3 right, float maxClearance, out Vector3 bestPoint)
        {
            bestPoint = left;
            if (maxClearance <= 0f) return 0f;

            var key = (left.X, left.Z, right.X, right.Z, (int)MathF.Round(maxClearance * 1000f));
            if (_portalCache.TryGetValue(key, out var cached))
            {
                bestPoint = cached.BestPoint;
                return cached.Clearance;
            }

            float best = 0f;
            for (int s = 0; s <= 4; s++)
            {
                float t = s * 0.25f;
                var p = new Vector3(left.X + (right.X - left.X) * t, left.Y, left.Z + (right.Z - left.Z) * t);
                float clearance = ClearanceAtPoint(p, maxClearance);
                if (clearance > best) { best = clearance; bestPoint = p; }
                if (best >= maxClearance) break;
            }

            if (_portalCache.Count >= PortalCacheLimit) _portalCache.Clear(); // 防 bodyRadius 连续变化时无界增长
            _portalCache[key] = (best, bestPoint);
            return best;
        }

        private float ClearanceAtPoint(Vector3 point, float maxClearance)
        {
            if (!_graph.IsPointNavigable(point, 0f)) return 0f;
            if (_graph.IsPointNavigable(point, maxClearance)) return maxClearance;
            float lo = 0f, hi = maxClearance;
            for (int i = 0; i < 5; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (_graph.IsPointNavigable(point, mid)) lo = mid; else hi = mid;
            }
            return lo;
        }

        private bool ValidatePath(List<Vector3> points, float bodyRadius)
        {
            for (int i = 1; i < points.Count; i++)
                if (!IsValidSegment(points[i - 1], points[i], bodyRadius, points[0], points[^1]))
                    return false;
            return true;
        }

        private bool IsValidSegment(Vector3 a, Vector3 b, float bodyRadius, Vector3 pathStart, Vector3 pathGoal)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / ValidationStep));
            for (int i = 0; i <= steps; i++)
            {
                if ((i & 15) == 0 && BudgetExceeded()) return false;
                float t = i / (float)steps;
                var p = new Vector3(a.X + dx * t, a.Y, a.Z + dz * t);
                // 放宽按整条路径的首尾计算，而不是每段局部距离：
                // 1m 内最多按半身半径；距端点不足 0.5m 时线性收紧到 0
                // （起点投影可能恰在网格边界上，采样点不可能立刻达到半身半径）。
                float pathEndpointDistance = MathF.Min(DistanceXZ(p, pathStart), DistanceXZ(p, pathGoal));
                float radius;
                if (pathEndpointDistance <= EndpointEpsilon) radius = 0f;
                else if (pathEndpointDistance < 1f) radius = bodyRadius * MathF.Min(0.5f, pathEndpointDistance);
                else radius = bodyRadius;
                if (!_graph.IsPointNavigable(p, radius)) return false;
            }
            return true;
        }

        private List<Vector3> BuildClearanceCandidates(List<(Vector3 Left, Vector3 Right)> portals,
            Vector3 pathStart, Vector3 pathGoal, float bodyRadius)
        {
            var candidates = new List<Vector3>(portals.Count * 2 + 2) { pathStart };
            for (int i = 0; i < portals.Count; i++)
            {
                PortalClearance(portals[i].Left, portals[i].Right, bodyRadius, out Vector3 bestPoint);
                candidates.Add(bestPoint);

                // 相邻两门的共享顶点就是走廊墙角；贴墙时加一个沿角平分线外推的偏移点，
                // 因为“门内收缩点”之间的直线仍会从墙角内侧切过去。
                if (i + 1 < portals.Count &&
                    TryFindSharedCorner(portals[i], portals[i + 1], out Vector3 corner, out Vector3 otherA, out Vector3 otherB) &&
                    !_graph.IsPointNavigable(corner, bodyRadius) &&
                    TryCornerOffsetPoint(corner, otherA, otherB, bodyRadius, out Vector3 offset))
                {
                    candidates.Add(offset);
                }
            }
            candidates.Add(pathGoal);
            return candidates;
        }

        private static bool TryFindSharedCorner((Vector3 Left, Vector3 Right) a, (Vector3 Left, Vector3 Right) b,
            out Vector3 corner, out Vector3 otherA, out Vector3 otherB)
        {
            corner = otherA = otherB = default;
            if (SameXZ(a.Left, b.Left)) { corner = a.Left; otherA = a.Right; otherB = b.Right; return true; }
            if (SameXZ(a.Left, b.Right)) { corner = a.Left; otherA = a.Right; otherB = b.Left; return true; }
            if (SameXZ(a.Right, b.Left)) { corner = a.Right; otherA = a.Left; otherB = b.Right; return true; }
            if (SameXZ(a.Right, b.Right)) { corner = a.Right; otherA = a.Left; otherB = b.Left; return true; }
            return false;
        }

        private bool TryCornerOffsetPoint(Vector3 corner, Vector3 otherA, Vector3 otherB, float bodyRadius, out Vector3 offset)
        {
            offset = default;
            float ax = otherA.X - corner.X, az = otherA.Z - corner.Z;
            float bx = otherB.X - corner.X, bz = otherB.Z - corner.Z;
            float lengthA = MathF.Sqrt(ax * ax + az * az);
            float lengthB = MathF.Sqrt(bx * bx + bz * bz);
            if (lengthA <= 1e-5f || lengthB <= 1e-5f) return false;

            float dx = ax / lengthA + bx / lengthB;
            float dz = az / lengthA + bz / lengthB;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            if (length <= 1e-5f) return false;
            dx /= length;
            dz /= length;

            for (float factor = 1.25f; factor <= 2f + 1e-4f; factor += 0.25f)
            {
                var p = new Vector3(corner.X + dx * bodyRadius * factor, corner.Y,
                    corner.Z + dz * bodyRadius * factor);
                if (_graph.IsPointNavigable(p, bodyRadius)) { offset = p; return true; }
            }
            return false;
        }

        private static bool SameXZ(Vector3 a, Vector3 b)
            => MathF.Abs(a.X - b.X) <= 1e-3f && MathF.Abs(a.Z - b.Z) <= 1e-3f;

        private List<Vector3> ShortcutPath(List<Vector3> candidates, float bodyRadius)
        {
            Vector3 pathStart = candidates[0], pathGoal = candidates[^1];
            var result = new List<Vector3> { pathStart };
            int anchor = 0;
            while (anchor < candidates.Count - 1)
            {
                int next = anchor + 1;
                for (int i = candidates.Count - 1; i > anchor + 1; i--)
                {
                    if (!IsValidSegment(candidates[anchor], candidates[i], bodyRadius, pathStart, pathGoal))
                        continue;
                    next = i;
                    break;
                }
                result.Add(candidates[next]);
                anchor = next;
            }
            return result;
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
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

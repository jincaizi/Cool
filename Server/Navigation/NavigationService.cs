using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KcpServer.Config;

namespace KcpServer.Navigation
{
    public readonly struct NavQuery
    {
        public readonly string MapId;
        public readonly Vector3 Start;
        public readonly Vector3 Goal;
        public readonly float BodyRadius;
        public readonly int MaxExpandedNodes;

        public NavQuery(string mapId, Vector3 start, Vector3 goal, float bodyRadius, int maxExpandedNodes = 50000)
        {
            MapId = mapId;
            Start = start;
            Goal = goal;
            BodyRadius = bodyRadius;
            MaxExpandedNodes = maxExpandedNodes;
        }
    }

    public sealed class NavigationService
    {
        private readonly NavMeshGraph _graph;
        private readonly NavMeshPathfinder _pathfinder;
        private readonly Stopwatch _tickClock = new Stopwatch();
        private int _queriesThisTick;
        private uint? _lastTick;
        private double _lastTickMilliseconds;
        private long _budgetRejections;

        public string MapId { get; }
        public int MaxQueriesPerTick { get; set; } = 16;

        /// <summary>每 tick 寻路的时间预算（毫秒）。次数上限之外再加一道时间闸门：
        /// 单次查询 2–8ms 时 16 次能吃掉 100ms+，只数次数保证不了 tick 不超时。
        /// 超限的查询返回 <c>FailReason="budget"</c>，由 PathFollower 下 tick 重试。</summary>
        public int MaxMillisecondsPerTick { get; set; } = 25;

        public int QueryCount { get; private set; }

        /// <summary>因次数/时间预算被拒绝的查询数（benchmark/诊断口径）。</summary>
        public long BudgetRejections => _budgetRejections;

        /// <summary>上一个已完成 tick 的寻路耗时（毫秒）；BeginTick 换 tick 时结算。</summary>
        public double LastTickMilliseconds => _lastTickMilliseconds;

        public NavigationService(NavMeshGraph graph, string mapId)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _pathfinder = new NavMeshPathfinder(graph);
            MapId = mapId ?? string.Empty;
        }

        public NavigationService(NavData data)
            : this(NavMeshGraph.FromData(data), data.MapId)
        {
        }

        public static NavigationService LoadFromConfig(string mapsConfigPath, string mapId, string baseDirectory)
        {
            MapsConfig maps = ConfigLoader.LoadMaps(mapsConfigPath);
            MapEntry? entry = maps.maps.FirstOrDefault(m => m.id == mapId);
            if (entry == null) throw new InvalidOperationException($"map '{mapId}' not found in {mapsConfigPath}");

            string navPath = Path.Combine(baseDirectory, entry.navFile);
            NavData data = NavDataFormat.Read(navPath);
            if (data.MapId != mapId)
                throw new InvalidDataException($"map id mismatch: config={mapId} file={data.MapId}");
            return new NavigationService(data);
        }

        /// <summary>开始新 tick：结算上一 tick 耗时、清零次数与计时。**同一 tick 重复调用幂等**——
        /// 否则调用方多叫一次就会白送一份预算（MonsterModule 与测试宿主可能都会叫）。</summary>
        public void BeginTick(uint tick)
        {
            if (_lastTick == tick) return;
            if (_lastTick.HasValue) _lastTickMilliseconds = _tickClock.Elapsed.TotalMilliseconds;
            _lastTick = tick;
            _queriesThisTick = 0;
            _tickClock.Restart();
        }

        public bool TryFindPath(NavQuery query, out NavPath path)
        {
            if (query.MapId != MapId)
            {
                path = NavPath.Fail("wrong-map");
                return false;
            }
            if (_queriesThisTick >= MaxQueriesPerTick
                || _tickClock.Elapsed.TotalMilliseconds >= MaxMillisecondsPerTick)
            {
                _budgetRejections++;
                path = NavPath.Fail("budget");
                return false;
            }

            _queriesThisTick++;
            QueryCount++;
            // 把本 tick 剩余预算传进单次查询：长距离/Boss 大半径查询可以在半途以 "budget"
            // 退出（下 tick 重试，portal 缓存已积累），保证单条查询不会把 tick 拖过 66ms。
            double remainingMs = MaxMillisecondsPerTick - _tickClock.Elapsed.TotalMilliseconds;
            path = _pathfinder.FindPath(query.Start, query.Goal, query.BodyRadius, query.MaxExpandedNodes, remainingMs);
            if (!path.Success && path.FailReason == "budget") _budgetRejections++;
            return path.Success;
        }

        public bool TryProject(Vector3 point, float maxDistance, out Vector3 projected, out float clearance)
        {
            projected = point;
            clearance = 0f;
            if (!_graph.FindNearestPoint(point, maxDistance, out projected, out _, out _))
                return false;
            // 点级 clearance（到最近边界边的精确距离）。三角面级取质心/边中点最小值，
            // 在尖角处会低估、在狭长三角形里会高估巡逻点的实际可站立空间。
            clearance = _graph.GetPointClearance(projected);
            return true;
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to, float bodyRadius)
            => _graph.HasLineOfSight(from, to, bodyRadius);
    }
}

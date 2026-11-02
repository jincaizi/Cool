using System;
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
        private int _queriesThisTick;

        public string MapId { get; }
        public int MaxQueriesPerTick { get; set; } = 16;
        public int QueryCount { get; private set; }

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

        public void BeginTick(uint tick) => _queriesThisTick = 0;

        public bool TryFindPath(NavQuery query, out NavPath path)
        {
            if (query.MapId != MapId)
            {
                path = NavPath.Fail("wrong-map");
                return false;
            }
            if (_queriesThisTick >= MaxQueriesPerTick)
            {
                path = NavPath.Fail("budget");
                return false;
            }

            _queriesThisTick++;
            QueryCount++;
            path = _pathfinder.FindPath(query.Start, query.Goal, query.BodyRadius, query.MaxExpandedNodes);
            return path.Success;
        }

        public bool TryProject(Vector3 point, float maxDistance, out Vector3 projected, out float clearance)
        {
            projected = point;
            clearance = 0f;
            if (!_graph.FindNearestPoint(point, maxDistance, out projected, out _, out int triangle))
                return false;
            clearance = _graph.GetClearance(triangle);
            return true;
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to, float bodyRadius)
            => _graph.HasLineOfSight(from, to, bodyRadius);
    }
}

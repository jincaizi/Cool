using System;
using KcpServer.Navigation;

namespace KcpServer.Monster
{
    // P1 用同步预算（NavigationService.TryFindPath 按 tick 计数），请求不排队，
    // 因此没有“过期请求”需要丢弃；失败后按 FailureBackoffTicks=30 退避。
    public sealed class PathFollower
    {
        private const uint RepathIntervalTicks = 15;
        private const uint FailureBackoffTicks = 30;
        private const float TargetMoveThreshold = 2f;
        private const float StuckWindowTicks = 10;
        private const int StuckWindowLimit = 3;

        private readonly NavigationService? _navigation;
        private NavPath? _path;
        private int _index;
        private Vector3 _goal;
        private uint _nextRepathTick;
        private uint _nextAttemptTick;
        private Vector3 _stuckCheckPosition;
        private uint _stuckCheckTick;
        private int _stuckWindows;

        public PathFollower(NavigationService? navigation) => _navigation = navigation;

        /// <summary>无导航时保持旧行为（不阻断攻击）。</summary>
        public bool HasLineOfSight(Vector3 from, Vector3 to, float bodyRadius)
            => _navigation == null || _navigation.HasLineOfSight(from, to, bodyRadius);

        /// <summary>停步判定用 navmesh 投影点；无导航时原样返回。</summary>
        public bool TryProject(Vector3 point, out Vector3 projected)
        {
            projected = point;
            return _navigation != null && _navigation.TryProject(point, 2f, out projected, out _);
        }

        // 状态观察口（Task 7 状态机接入前暂无调用者；HasPath 供卡死/重寻测试观察）。
        public bool HasPath => _path != null;
        public int WaypointIndex => _index;

        public void Tick(MonsterState monster, Vector3 goal, SimpleMoveSystem mover, float deltaTime, uint tick)
        {
            if (_navigation == null)
            {
                mover.MoveTo(monster, goal, deltaTime);
                return;
            }

            bool goalMoved = DistanceXZ(goal, _goal) > TargetMoveThreshold;
            bool due = tick >= _nextRepathTick;
            bool noPath = _path == null;
            if ((noPath || due || goalMoved) && tick >= _nextAttemptTick)
            {
                var query = new NavQuery(_navigation.MapId, monster.Position, goal, monster.Config.bodyRadius);
                if (_navigation.TryFindPath(query, out NavPath path))
                {
                    _path = path;
                    _goal = goal;
                    _index = 0;
                    // 相位错峰：200 只怪按 npcId 分散首次周期重寻，避免预算被同步波压垮。
                    // 不推进 _nextAttemptTick——否则 goalMoved 会被 15 tick 间隔闸门挡成死代码；
                    // goalMoved 自身有 2m 阈值，成功后 _goal 更新即复位，不会每 tick 重寻。
                    uint phase = (uint)(monster.NpcId % RepathIntervalTicks);
                    _nextRepathTick = tick + RepathIntervalTicks + phase;
                    _stuckCheckPosition = monster.Position;
                    _stuckCheckTick = tick;
                    _stuckWindows = 0;
                    SkipPassedWaypoints(monster);
                }
                else
                {
                    // 预算拒绝不是寻路失败：下 tick 立刻重试；真正失败才退避。
                    _nextAttemptTick = path.FailReason == "budget" ? tick + 1 : tick + FailureBackoffTicks;
                }
            }

            if (_path == null)
            {
                mover.MoveTo(monster, goal, deltaTime);
                return;
            }

            AdvanceWaypoints(monster);
            if (_index >= _path.Waypoints.Length)
            {
                mover.MoveTo(monster, goal, deltaTime);
                return;
            }

            Vector3 waypoint = _path.Waypoints[_index];
            // index 0 没有“上一路点”（Waypoints[0] 就是投影起点），线段退化成点：
            // 起点在 navmesh 外 1.5–2m 时会每 tick 重寻。此时跳过偏离判定，先走向投影起点。
            if (_index > 0 && DistancePointSegment(monster.Position, _path.Waypoints[_index - 1], waypoint) > 1.5f)
            {
                _path = null;
                _nextRepathTick = tick;
                _nextAttemptTick = tick;
                mover.MoveTo(monster, goal, deltaTime);
                return;
            }

            Vector3 direction = waypoint - monster.Position;
            direction.Y = 0f;
            mover.MoveTowards(monster, direction, deltaTime);

            CheckStuck(monster, goal, tick);
        }

        // 执行偏差（Task 7 集成暴露）：原定 max(bodyRadius*0.5, moveSpeed*0.25) 对 Wolf（3.5 m/s）
        // 是 0.875m，会在墙角路点前 0.86m 提前跳点、直线切过墙格。改为只看 bodyRadius*0.5。
        // 若将来要恢复 moveSpeed 项，必须证明/实现额外的墙角保护（例如推进前校验下一段 LOS）。
        private void AdvanceWaypoints(MonsterState monster)
        {
            float radius = monster.Config.bodyRadius * 0.5f;
            int guard = 0;
            while (_index < _path!.Waypoints.Length && guard++ < _path.Waypoints.Length + 1)
            {
                Vector3 point = _path.Waypoints[_index];
                if (DistanceXZ(monster.Position, point) <= radius)
                {
                    _index++;
                    continue;
                }

                Vector3 next = _index + 1 < _path.Waypoints.Length ? _path.Waypoints[_index + 1] : _path.Waypoints[^1];
                float vx = monster.Position.X - point.X, vz = monster.Position.Z - point.Z;
                float wx = next.X - point.X, wz = next.Z - point.Z;
                if (vx * wx + vz * wz > 0f)
                {
                    _index++;
                    continue;
                }
                break;
            }
        }

        private void SkipPassedWaypoints(MonsterState monster)
        {
            float radius = monster.Config.bodyRadius * 0.5f;
            while (_index < _path!.Waypoints.Length && DistanceXZ(monster.Position, _path.Waypoints[_index]) <= radius)
                _index++;
        }

        private void CheckStuck(MonsterState monster, Vector3 goal, uint tick)
        {
            if (tick - _stuckCheckTick < StuckWindowTicks) return;

            float moved = DistanceXZ(monster.Position, _stuckCheckPosition);
            bool inAttackRange = DistanceXZ(monster.Position, goal) <= monster.Config.attackRange;
            if (moved < 0.2f && !inAttackRange) _stuckWindows++;
            else _stuckWindows = 0;

            _stuckCheckPosition = monster.Position;
            _stuckCheckTick = tick;

            if (_stuckWindows >= StuckWindowLimit)
            {
                _path = null;
                _nextRepathTick = tick;
                _nextAttemptTick = tick;
                _stuckWindows = 0;
            }
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static float DistancePointSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq <= 0.000001f ? 0f : ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float x = a.X + dx * t, z = a.Z + dz * t;
            float ex = p.X - x, ez = p.Z - z;
            return MathF.Sqrt(ex * ex + ez * ez);
        }
    }
}

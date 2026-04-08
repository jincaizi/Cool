using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KcpServer;
using KcpServer.Config;
using KcpServer.AI.BehaviorTree;
using KcpServer.AI.Movement;

namespace KcpServer.AI.Core
{
    public class AiManager
    {
        private readonly Dictionary<long, AiComponent> _aiComponents = new();
        private readonly Dictionary<int, MonsterData> _monsterConfigs = new();
        private long _nextInstanceId = 1;
        private readonly object _lock = new();
        private bool _running;
        private Task? _updateTask;
        private readonly Dictionary<long, Vector3> _lastPositions = new();

        // Player position provider (set by integration)
        private Func<long, IEnumerable<(long playerId, Vector3 position)>>? _playerPositionProvider;

        public event Action<long, Vector3, Quaternion, NpcAnimationState>? OnAiStateChanged;

        public AiManager(string? configPath = null)
        {
            LoadMonsterConfigs(configPath);
        }

        /// <summary>
        /// Set the function to get player positions for target detection
        /// </summary>
        public void SetPlayerPositionProvider(Func<long, IEnumerable<(long playerId, Vector3 position)>> provider)
        {
            _playerPositionProvider = provider;
        }

        private void LoadMonsterConfigs(string? configPath)
        {
            string path = configPath ?? "Config/monster_config.json";
            _monsterConfigs.Clear();

            var templates = ConfigLoader.LoadMonsterTemplates(path);
            foreach (var kvp in templates)
            {
                _monsterConfigs[kvp.Key] = kvp.Value;
            }

            Console.WriteLine($"Loaded {_monsterConfigs.Count} monster templates");
        }

        public AiComponent Spawn(int templateId, Vector3 position, Quaternion rotation)
        {
            lock (_lock)
            {
                if (!_monsterConfigs.TryGetValue(templateId, out var config))
                {
                    throw new ArgumentException($"Unknown monster template: {templateId}");
                }

                long id = _nextInstanceId++;
                var ai = new AiComponent(id, templateId, config);
                ai.Position = position;
                ai.Rotation = rotation;
                ai.Blackboard.SpawnPosition = position;
                ai.Blackboard.PatrolCenter = position;

                ai.BehaviorTree = BuildBehaviorTree(ai);
                ai.PatrolPoints = GeneratePatrolPoints(position, ai.Blackboard.PatrolRadius);

                _aiComponents[id] = ai;
                Console.WriteLine($"Spawned AI {id} (template {templateId}) at {position}");
                return ai;
            }
        }

        public void Despawn(long instanceId)
        {
            lock (_lock)
            {
                if (_aiComponents.Remove(instanceId))
                {
                    _lastPositions.Remove(instanceId);
                    Console.WriteLine($"Despawned AI {instanceId}");
                }
            }
        }

        public void Start()
        {
            _running = true;
            _updateTask = Task.Run(() => UpdateLoop());
            Console.WriteLine("AiManager started");
        }

        public void Stop()
        {
            _running = false;
            _updateTask?.Wait();
            Console.WriteLine("AiManager stopped");
        }

        private void UpdateLoop()
        {
            while (_running)
            {
                float deltaTime = 0.1f;
                List<AiComponent> ais;

                lock (_lock)
                {
                    ais = new List<AiComponent>(_aiComponents.Values);
                }

                foreach (var ai in ais)
                {
                    try
                    {
                        // Target detection
                        DetectTargets(ai);

                        var prevAnim = ai.CurrentAnimState;
                        ai.Update(deltaTime);

                        bool posChanged = !_lastPositions.TryGetValue(ai.InstanceId, out var lastPos) ||
                                          Vector3.Distance(ai.Position, lastPos) > 0.1f;

                        if (ai.CurrentAnimState != prevAnim || posChanged)
                        {
                            OnAiStateChanged?.Invoke(ai.InstanceId, ai.Position, ai.Rotation, ai.CurrentAnimState);
                            _lastPositions[ai.InstanceId] = ai.Position;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"AI Update error: {e.Message}");
                    }
                }

                Thread.Sleep(100); // 10Hz
            }
        }

        private void DetectTargets(AiComponent ai)
        {
            if (_playerPositionProvider == null) return;

            // Get all nearby players
            var players = _playerPositionProvider(ai.InstanceId);
            if (players == null) return;

            long? closestPlayerId = null;
            Vector3? closestPosition = null;
            float closestDistance = float.MaxValue;

            foreach (var (playerId, position) in players)
            {
                if (ai.TargetDetector.CanDetectTarget(ai.Position, ai.Rotation, position))
                {
                    float dist = Vector3.Distance(ai.Position, position);
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        closestPlayerId = playerId;
                        closestPosition = position;
                    }
                }
            }

            if (closestPlayerId.HasValue)
            {
                if (!ai.Blackboard.TargetId.HasValue || ai.Blackboard.TargetId != closestPlayerId)
                {
                    // New target acquired
                    ai.SetTarget(closestPlayerId, closestPosition);
                    Console.WriteLine($"AI {ai.InstanceId} acquired target: player {closestPlayerId}");
                }
                else
                {
                    // Update last known position
                    ai.Blackboard.LastKnownTargetPosition = closestPosition;
                }
            }
            else if (ai.Blackboard.TargetId.HasValue)
            {
                // Target lost
                ai.SetTarget(null);
                Console.WriteLine($"AI {ai.InstanceId} lost target");
            }
        }

        private Vector3[] GeneratePatrolPoints(Vector3 center, float radius)
        {
            const int count = 4;
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * (360f / count);
                float rad = angle * (float)(System.Math.PI / 180.0);
                points[i] = new Vector3(
                    center.X + (float)System.Math.Cos(rad) * radius,
                    center.Y,
                    center.Z + (float)System.Math.Sin(rad) * radius
                );
            }
            return points;
        }

        private BtNode BuildBehaviorTree(AiComponent ai)
        {
            var moveSystem = new SimpleMoveSystem();

            return new BtSelector(
                new BtSequence(
                    new BtCondition(ai => ai.Blackboard.AlertLevel == AlertLevel.PEACE),
                    new BtCondition(ai => !ai.Blackboard.TargetId.HasValue),
                    new BtAction(ai => BtActions.Patrol(ai, moveSystem, ai.PatrolPoints))
                ),
                new BtSequence(
                    new BtCondition(ai => ai.Blackboard.AlertLevel == AlertLevel.HOSTILE),
                    new BtCondition(ai => ai.Blackboard.TargetId.HasValue),
                    new BtAction(ai => BtActions.Chase(ai, ai.Blackboard.LastKnownTargetPosition ?? ai.Position, moveSystem)),
                    new BtAction(ai => BtActions.Attack(ai, ai.Blackboard.LastKnownTargetPosition ?? ai.Position, ai.SkillSystem))
                ),
                new BtSequence(
                    new BtCondition(ai => !ai.Blackboard.TargetId.HasValue),
                    new BtCondition(ai => ai.Blackboard.AlertLevel != AlertLevel.PEACE),
                    new BtAction(ai => BtActions.Return(ai, moveSystem))
                )
            );
        }

        public int AiCount
        {
            get
            {
                lock (_lock)
                {
                    return _aiComponents.Count;
                }
            }
        }
    }
}

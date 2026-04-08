using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KcpServer;
using KcpServer.Config;

namespace KcpServer.AI.Core
{
    public class AiManager
    {
        private readonly Dictionary<long, AiComponent> _aiComponents = new();
        private long _nextInstanceId = 1;
        private readonly object _lock = new();
        private bool _running;
        private Task? _updateTask;
        private readonly Dictionary<long, Vector3> _lastPositions = new();

        // Config
        private readonly Dictionary<int, MonsterData> _monsterConfigs = new();

        public event Action<long, Vector3, Quaternion, NpcAnimationState>? OnAiStateChanged;

        public AiManager()
        {
            // Load configs
            LoadMonsterConfigs();
        }

        private void LoadMonsterConfigs()
        {
            // Default configs - in production would load from JSON
            var slime = new MonsterData
            {
                templateId = 1,
                name = "Slime",
                hp = 100,
                moveSpeed = 2.0f,
                detectionRadius = 8,
                visionAngle = 120,
                attackRange = 1.5f,
                patrolRadius = 5,
                skills = new List<string> { "Attack" }
            };

            var wolf = new MonsterData
            {
                templateId = 2,
                name = "Wolf",
                hp = 150,
                moveSpeed = 3.5f,
                detectionRadius = 15,
                visionAngle = 90,
                attackRange = 2.0f,
                patrolRadius = 10,
                skills = new List<string> { "Attack" }
            };

            _monsterConfigs[1] = slime;
            _monsterConfigs[2] = wolf;
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

                // Build behavior tree
                ai.BehaviorTree = BuildBehaviorTree(ai);

                // Generate patrol points
                ai.PatrolPoints = GeneratePatrolPoints(position, ai.Blackboard.PatrolRadius);

                _aiComponents[id] = ai;
                return ai;
            }
        }

        public void Despawn(long instanceId)
        {
            lock (_lock)
            {
                _aiComponents.Remove(instanceId);
                _lastPositions.Remove(instanceId);
            }
        }

        public void Start()
        {
            _running = true;
            _updateTask = Task.Run(() => UpdateLoop());
        }

        public void Stop()
        {
            _running = false;
            _updateTask?.Wait();
        }

        private void UpdateLoop()
        {
            while (_running)
            {
                float deltaTime = 0.1f; // 10Hz update rate
                List<AiComponent> ais;

                lock (_lock)
                {
                    ais = new List<AiComponent>(_aiComponents.Values);
                }

                foreach (var ai in ais)
                {
                    try
                    {
                        var prevAnim = ai.CurrentAnimState;
                        ai.Update(deltaTime);

                        // Check if position or animation changed
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
            var moveSystem = new Movement.SimpleMoveSystem();

            // Root: Selector
            // ├── Sequence: 巡逻（PEACE）
            // ├── Sequence: 追击+攻击（HOSTILE）
            // └── Sequence: 返回
            return new BehaviorTree.BtSelector(
                // Patrol sequence
                new BehaviorTree.BtSequence(
                    new BehaviorTree.BtCondition(ai => ai.Blackboard.AlertLevel == AlertLevel.PEACE),
                    new BehaviorTree.BtCondition(ai => !ai.Blackboard.TargetId.HasValue),
                    new BehaviorTree.BtAction(ai => BtActions.Patrol(ai, moveSystem, ai.PatrolPoints))
                ),
                // Chase and Attack sequence
                new BehaviorTree.BtSequence(
                    new BehaviorTree.BtCondition(ai => ai.Blackboard.AlertLevel == AlertLevel.HOSTILE),
                    new BehaviorTree.BtCondition(ai => ai.Blackboard.TargetId.HasValue),
                    new BehaviorTree.BtAction(ai => BtActions.Chase(ai, ai.Blackboard.LastKnownTargetPosition ?? ai.Position, moveSystem)),
                    new BehaviorTree.BtAction(ai => BtActions.Attack(ai, ai.Blackboard.LastKnownTargetPosition ?? ai.Position, ai.SkillSystem))
                ),
                // Return sequence
                new BehaviorTree.BtSequence(
                    new BehaviorTree.BtCondition(ai => !ai.Blackboard.TargetId.HasValue),
                    new BehaviorTree.BtCondition(ai => ai.Blackboard.AlertLevel != AlertLevel.PEACE),
                    new BehaviorTree.BtAction(ai => BtActions.Return(ai, moveSystem))
                )
            );
        }
    }
}

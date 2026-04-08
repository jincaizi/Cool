using System.Collections.Generic;
using KcpServer;
using KcpServer.AI.Combat;
using KcpServer.AI.Detection;
using KcpServer.AI.Movement;
using KcpServer.AI.Skill;
using KcpServer.AI.BehaviorTree;
using KcpServer.Config;

namespace KcpServer.AI.Core
{
    public sealed class AiComponent
    {
        public long InstanceId { get; }
        public int TemplateId { get; }

        public AiBlackboard Blackboard { get; } = new();
        public BtNode? BehaviorTree { get; set; }
        public SkillSystem SkillSystem { get; }
        public AggroTable AggroTable { get; } = new();
        public TargetDetector TargetDetector { get; }

        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public float MoveSpeed { get; set; }
        public float VisionRadius { get; set; }
        public float VisionAngle { get; set; }
        public float AttackRange { get; set; }
        public NpcAnimationState CurrentAnimState { get; set; } = NpcAnimationState.Idle;
        public Vector3[] PatrolPoints { get; set; } = System.Array.Empty<Vector3>();

        private readonly SimpleMoveSystem _moveSystem = new();
        private long? _currentTargetId;

        public AiComponent(long instanceId, int templateId, MonsterData config)
        {
            InstanceId = instanceId;
            TemplateId = templateId;
            MoveSpeed = config.moveSpeed;
            VisionRadius = config.detectionRadius;
            VisionAngle = config.visionAngle;
            AttackRange = config.attackRange;

            Blackboard.SpawnPosition = Vector3.zero;
            Blackboard.PatrolCenter = Vector3.zero;
            Blackboard.PatrolRadius = config.patrolRadius;

            // Init skills
            var skills = new List<SkillData>();
            foreach (var skillName in config.skills)
            {
                skills.Add(new SkillData { SkillName = skillName, Damage = 10, Range = config.attackRange, Cooldown = 1f });
            }
            SkillSystem = new SkillSystem(skills);

            TargetDetector = new TargetDetector(config.detectionRadius, config.visionAngle);
        }

        public void Update(float deltaTime)
        {
            // Update skill cooldowns
            SkillSystem.Update(deltaTime);

            // Update aggro decay
            bool targetInRange = _currentTargetId.HasValue;
            AggroTable.DecayAll(deltaTime, targetInRange);

            // Execute behavior tree
            if (BehaviorTree != null)
            {
                BehaviorTree.Tick(this);
            }
        }

        public void SetTarget(long? targetId, Vector3? targetPosition = null)
        {
            _currentTargetId = targetId;
            Blackboard.TargetId = targetId;
            if (targetId.HasValue)
            {
                Blackboard.AlertLevel = AlertLevel.HOSTILE;
                Blackboard.LastKnownTargetPosition = targetPosition;
            }
        }
    }
}

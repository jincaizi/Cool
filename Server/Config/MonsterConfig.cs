using System.Collections.Generic;

namespace KcpServer.Config
{
    public class MonsterConfig
    {
        public List<MonsterData> monsters { get; set; } = new();
    }

    public class MonsterData
    {
        public int templateId;
        public string name;
        public float hp;
        public float moveSpeed;
        public float detectionRadius;
        public float visionAngle;
        public float attackRange;
        public float patrolRadius;
        public List<string> skills;
    }
}
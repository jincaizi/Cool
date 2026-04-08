using KcpServer;
using KcpServer.AI.Core;
using KcpServer.AI.BehaviorTree;
using KcpServer.AI.Movement;
using KcpServer.AI.Skill;

namespace KcpServer.AI
{
    public static class BtActions
    {
        public static BtStatus Patrol(AiComponent ai, SimpleMoveSystem moveSystem, Vector3[] patrolPoints)
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
                return BtStatus.Success;

            int idx = ai.Blackboard.CurrentPatrolIndex % patrolPoints.Length;
            Vector3 target = patrolPoints[idx];

            moveSystem.MoveTo(ai, target, 0.1f); // deltaTime = 0.1 for 10Hz update

            if (moveSystem.HasReached(ai, target))
            {
                ai.Blackboard.CurrentPatrolIndex++;
                ai.CurrentAnimState = NpcAnimationState.Idle;
            }
            else
            {
                ai.CurrentAnimState = NpcAnimationState.Run;
            }

            return BtStatus.Running;
        }

        public static BtStatus Chase(AiComponent ai, Vector3 targetPosition, SimpleMoveSystem moveSystem)
        {
            moveSystem.MoveTo(ai, targetPosition, 0.1f);
            ai.CurrentAnimState = NpcAnimationState.Run;
            return BtStatus.Running;
        }

        public static BtStatus Attack(AiComponent ai, Vector3 targetPosition, SkillSystem skillSystem)
        {
            float distance = Vector3.Distance(ai.Position, targetPosition);
            if (distance > ai.AttackRange)
                return BtStatus.Failure; // target out of range, stop attacking

            var damage = skillSystem.CastSkill("Attack");
            if (damage.HasValue)
            {
                ai.CurrentAnimState = NpcAnimationState.Attack;
                return BtStatus.Success;
            }

            return BtStatus.Running; // on cooldown, keep trying
        }

        public static BtStatus Return(AiComponent ai, SimpleMoveSystem moveSystem)
        {
            moveSystem.MoveTo(ai, ai.Blackboard.SpawnPosition, 0.1f);

            if (moveSystem.HasReached(ai, ai.Blackboard.SpawnPosition))
            {
                ai.Blackboard.AlertLevel = AlertLevel.PEACE;
                ai.CurrentAnimState = NpcAnimationState.Idle;
                return BtStatus.Success;
            }

            ai.CurrentAnimState = NpcAnimationState.Run;
            return BtStatus.Running;
        }
    }
}

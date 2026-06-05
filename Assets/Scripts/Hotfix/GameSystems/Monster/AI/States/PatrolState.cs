using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    public class PatrolState : AIStateBase
    {
        private readonly List<Vector3> _patrolPoints = new List<Vector3>();
        private int _patrolIndex;

        public override MonsterAIState StateType => MonsterAIState.Patrol;

        // Generate patrol points. Called externally after spawn point is known.
        public void GeneratePatrolPoints(Vector3 spawnPoint, float patrolRadius)
        {
            _patrolPoints.Clear();
            if (patrolRadius <= 0) return;
            for (int i = 0; i < 3; i++)
            {
                float angle = (360f / 3) * i * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * patrolRadius;
                _patrolPoints.Add(spawnPoint + offset);
            }
        }

        public override void OnEnter(AIContext ctx)
        {
            ctx.Animator.SetFloat(MonsterAnimHashes.Speed, 1f);
            if (_patrolPoints.Count > 0)
            {
                ctx.Movement.PatrolTo(_patrolPoints[_patrolIndex]);
                _patrolIndex = (_patrolIndex + 1) % _patrolPoints.Count;
            }
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            float distToTarget = ctx.Target != null
                ? Vector3.Distance(ctx.Self.position, ctx.Target.position)
                : float.MaxValue;

            if (distToTarget < ctx.Config.DetectRange)
                return MonsterAIState.Chase;

            if (ctx.Movement.HasReachedDestination)
                return MonsterAIState.Idle;

            return null;
        }
    }
}

using UnityEngine;
using UnityEngine.AI;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterMovement
    {
        private readonly NavMeshAgent _agent;
        private readonly Transform _self;
        private readonly MonsterConfig _config;

        public bool HasReachedDestination =>
            !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.1f;

        public MonsterMovement(NavMeshAgent agent, Transform self, MonsterConfig config)
        {
            _agent = agent;
            _self = self;
            _config = config;
            _agent.speed = config.MoveSpeed;
            _agent.stoppingDistance = config.AttackRange * 0.8f;
        }

        public void Stop()
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }

        public void Resume()
        {
            _agent.isStopped = false;
        }

        public void Chase(Transform target)
        {
            _agent.isStopped = false;
            _agent.SetDestination(target.position);
        }

        public void PatrolTo(Vector3 point)
        {
            _agent.isStopped = false;
            _agent.SetDestination(point);
        }

        public void ReturnToSpawn(Vector3 spawnPoint)
        {
            _agent.isStopped = false;
            _agent.SetDestination(spawnPoint);
        }

        public void LookAt(Vector3 target)
        {
            Vector3 dir = target - _self.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
            {
                _self.rotation = Quaternion.Slerp(
                    _self.rotation,
                    Quaternion.LookRotation(dir),
                    _config.RotationSpeed * Time.deltaTime);
            }
        }
    }
}

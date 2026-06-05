using UnityEngine;
using UnityEngine.AI;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterMovement
    {
        private readonly NavMeshAgent _agent;
        private readonly Transform _self;
        private readonly MonsterConfig _config;

        private Vector3 _knockbackVelocity;
        private float _knockbackTimer;

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
            // ResetPath clears any stale path from before the agent was stopped.
            // Without this, the agent may move 1 frame towards the old destination
            // before the new SetDestination overrides it.
            _agent.ResetPath();
            _agent.isStopped = false;
        }

        // Performance note: SetDestination is called every frame.
        // If this becomes a bottleneck with many active monsters (20+),
        // throttle to every 0.25s: cache last destination + only update
        // if target moved > 0.5m or timer elapsed.
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

        // ===== Knockback =====

        public void ApplyKnockback(Vector3 direction, float force)
        {
            if (force <= 0) return;
            _knockbackVelocity = direction.normalized * force;
            _knockbackTimer = _config.KnockbackDecay;
        }

        public Vector3 GetKnockbackDisplacement()
        {
            return _knockbackVelocity * Time.deltaTime;
        }

        public void UpdateKnockback(float deltaTime)
        {
            if (_knockbackTimer <= 0)
            {
                _knockbackVelocity = Vector3.zero;
                return;
            }

            _knockbackTimer -= deltaTime;
            float t = _config.KnockbackDecay > 0
                ? deltaTime / _config.KnockbackDecay
                : deltaTime / 0.5f;
            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, t);
            _self.position += _knockbackVelocity * deltaTime;
        }

        public void ResetKnockback()
        {
            _knockbackVelocity = Vector3.zero;
            _knockbackTimer = 0;
            // Sync NavMeshAgent to current position after knockback displacement.
            // Without this, the agent resumes from its last calculated position
            // which may be far from where knockback pushed the transform.
            // Only sync when enabled — disabled agents reject nextPosition (e.g., after death).
            if (_agent.enabled)
                _agent.nextPosition = _self.position;
        }
    }
}

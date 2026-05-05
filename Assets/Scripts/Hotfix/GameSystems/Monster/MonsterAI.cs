using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    public enum MonsterAIState
    {
        Idle = 0,
        Patrol = 1,
        Chase = 2,
        Attack = 3,
        Hit = 4,
        Death = 5
    }

    public class MonsterAI
    {
        private readonly MonsterMovement _movement;
        private readonly MonsterStats _stats;
        private readonly Animator _animator;
        private readonly Transform _self;
        private readonly MonsterConfig _config;
        private readonly Vector3 _spawnPoint;

        private MonsterAIState _state;
        private MonsterAIState _preHitState;
        private float _stateTimer;
        private float _attackCooldown;
        private int _patrolIndex;
        private readonly List<Vector3> _patrolPoints = new();

        private Transform _target;
        public Transform Target
        {
            get => _target;
            set
            {
                _target = value;
                if (value == null && _state != MonsterAIState.Death && _state != MonsterAIState.Hit)
                    ReturnToSpawn();
            }
        }

        public MonsterAIState CurrentState => _state;

        public event Action OnDeathComplete;
        public event Action OnAttackFrame;
        public event Action<MonsterAIState, MonsterAIState> OnStateChanged;

        private static readonly int HASH_State = Animator.StringToHash("AIState");
        private static readonly int HASH_Attack = Animator.StringToHash("Attack");
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");
        private static readonly int HASH_Death = Animator.StringToHash("Death");

        public MonsterAI(
            MonsterMovement movement, MonsterStats stats, Animator animator,
            Transform self, MonsterConfig config, Vector3 spawnPoint)
        {
            _movement = movement;
            _stats = stats;
            _animator = animator;
            _self = self;
            _config = config;
            _spawnPoint = spawnPoint;

            _state = MonsterAIState.Idle;
            _stateTimer = config.IdleDuration;
            GeneratePatrolPoints();
        }

        public void Update(float deltaTime)
        {
            if (_state == MonsterAIState.Death) return;

            _attackCooldown -= deltaTime;
            _stateTimer -= deltaTime;

            EvaluateTransitions();
            ExecuteState(deltaTime);
        }

        public void NotifyHit(DamageData damageData, Vector3 hitDirection)
        {
            if (_state == MonsterAIState.Death) return;

            _preHitState = _state == MonsterAIState.Hit ? _preHitState : _state;
            _stateTimer = _config.HitStunDuration;
            TransitionTo(MonsterAIState.Hit);
            _animator.SetTrigger(HASH_Hit);
            _movement.Stop();
        }

        public void EnterDeath()
        {
            _movement.Stop();
            TransitionTo(MonsterAIState.Death);
            _animator.SetTrigger(HASH_Death);
        }

        private void EvaluateTransitions()
        {
            float distToTarget = Target != null
                ? Vector3.Distance(_self.position, Target.position)
                : float.MaxValue;

            switch (_state)
            {
                case MonsterAIState.Idle:
                    if (distToTarget < _config.DetectRange)
                        TransitionTo(MonsterAIState.Chase);
                    else if (_patrolPoints.Count > 0 && _stateTimer <= 0)
                        TransitionTo(MonsterAIState.Patrol);
                    break;

                case MonsterAIState.Patrol:
                    if (distToTarget < _config.DetectRange)
                        TransitionTo(MonsterAIState.Chase);
                    else if (_movement.HasReachedDestination)
                        TransitionTo(MonsterAIState.Idle);
                    break;

                case MonsterAIState.Chase:
                    if (distToTarget > _config.LeaveRange)
                        ReturnToSpawn();
                    else if (distToTarget < _config.AttackRange && _attackCooldown <= 0)
                        TransitionTo(MonsterAIState.Attack);
                    else if (Target == null)
                        TransitionTo(MonsterAIState.Idle);
                    break;

                case MonsterAIState.Attack:
                    if (distToTarget > _config.LeaveRange)
                        ReturnToSpawn();
                    else if (distToTarget > _config.AttackRange)
                        TransitionTo(MonsterAIState.Chase);
                    break;

                case MonsterAIState.Hit:
                    if (_stats.IsDead)
                        EnterDeath();
                    else if (_stateTimer <= 0)
                        RecoverFromHit();
                    break;
            }
        }

        private void ExecuteState(float deltaTime)
        {
            switch (_state)
            {
                case MonsterAIState.Chase:
                    if (Target != null)
                    {
                        _movement.Chase(Target);
                        _movement.LookAt(Target.position);
                    }
                    break;

                case MonsterAIState.Attack:
                    _movement.Stop();
                    if (Target != null)
                        _movement.LookAt(Target.position);
                    break;
            }
        }

        private void TransitionTo(MonsterAIState newState)
        {
            if (_state == newState) return;

            var old = _state;
            _state = newState;
            _animator.SetInteger(HASH_State, (int)newState);
            OnStateChanged?.Invoke(old, newState);

            switch (newState)
            {
                case MonsterAIState.Idle:
                    _stateTimer = _config.IdleDuration;
                    break;

                case MonsterAIState.Patrol:
                    _movement.PatrolTo(_patrolPoints[_patrolIndex]);
                    _patrolIndex = (_patrolIndex + 1) % _patrolPoints.Count;
                    break;

                case MonsterAIState.Chase:
                    _movement.Resume();
                    break;

                case MonsterAIState.Attack:
                    _attackCooldown = _config.AttackCooldown;
                    _animator.SetTrigger(HASH_Attack);
                    OnAttackFrame?.Invoke();
                    break;
            }
        }

        private void RecoverFromHit()
        {
            if (Target != null)
            {
                float dist = Vector3.Distance(_self.position, Target.position);
                TransitionTo(dist < _config.AttackRange ? MonsterAIState.Attack : MonsterAIState.Chase);
            }
            else
            {
                var fallback = _preHitState == MonsterAIState.Hit || _preHitState == MonsterAIState.Death
                    ? MonsterAIState.Idle : _preHitState;
                TransitionTo(fallback);
            }
        }

        private void ReturnToSpawn()
        {
            Target = null;
            _movement.ReturnToSpawn(_spawnPoint);
            TransitionTo(MonsterAIState.Idle);
        }

        private void GeneratePatrolPoints()
        {
            _patrolPoints.Clear();
            if (_config.PatrolRadius <= 0) return;

            for (int i = 0; i < 3; i++)
            {
                float angle = (360f / 3) * i * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * _config.PatrolRadius;
                _patrolPoints.Add(_spawnPoint + offset);
            }
        }
    }
}

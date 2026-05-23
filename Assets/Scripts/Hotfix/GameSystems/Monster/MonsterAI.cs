using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Data;
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
        Death = 5,
        Defend = 6,
        Taunt = 7,
        Alert = 8,
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
        private int _currentAttackIndex;
        private bool _attackHitTarget;
        private float _defendChaseTimer;
        private Vector3 _lastHitDirection = Vector3.back;
        private float _lastKnockbackForce;

        private readonly List<Vector3> _patrolPoints = new();

        private DefendBehaviour _defend;
        private TauntBehaviour _taunt;
        private AlertBehaviour _alert;
        private IAttackShape _attackShape;

        private Transform _target;
        private readonly List<Hotfix.GameSystems.Sys3C.Core.Combat.IDamageable> _hitBuffer = new List<Hotfix.GameSystems.Sys3C.Core.Combat.IDamageable>(8);
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
        public Vector3 LastHitDirection => _lastHitDirection;
        public float LastKnockbackForce => _lastKnockbackForce;

        public event Action OnDeathComplete;
        public event Action<DamageBlock, EffectBlock> OnAttackHitboxActivate;
        public event Action OnAttackHitboxDeactivate;
        public event Action<MonsterAIState, MonsterAIState> OnStateChanged;

        private static readonly int HASH_AIState = Animator.StringToHash("AIState");
        private static readonly int HASH_Attack = Animator.StringToHash("Attack");
        private static readonly int HASH_AttackIndex = Animator.StringToHash("AttackIndex");
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");
        private static readonly int HASH_Death = Animator.StringToHash("Death");
        private static readonly int HASH_Taunt = Animator.StringToHash("Taunt");
        private static readonly int HASH_Defend = Animator.StringToHash("IsDefending");
        private static readonly int HASH_Speed = Animator.StringToHash("Speed");

        private readonly float _idleDuration;
        private readonly float _attackCooldownBase;
        private readonly float _patrolRadius;

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

            _idleDuration = RandomRange(config.IdleDuration, config.IdleDurationVariance);
            _attackCooldownBase = RandomRange(config.AttackCooldown, config.AttackCooldownVariance);
            _patrolRadius = RandomRange(config.PatrolRadius, config.PatrolRadiusVariance);

            _state = MonsterAIState.Idle;
            _stateTimer = _idleDuration;
            GeneratePatrolPoints();
            BuildBehaviours();
        }

        private static float RandomRange(float baseValue, float variance)
        {
            if (variance <= 0) return baseValue;
            return baseValue + UnityEngine.Random.Range(-variance, variance);
        }

        private void BuildBehaviours()
        {
            if (_config.EnableDefend)
                _defend = new DefendBehaviour();
            if (_config.EnableTaunt)
                _taunt = new TauntBehaviour();
            _alert = new AlertBehaviour();
        }

        public void Update(float deltaTime)
        {
            if (_state == MonsterAIState.Death)
            {
                _movement.UpdateKnockback(deltaTime);
                return;
            }

            _attackCooldown -= deltaTime;
            _stateTimer -= deltaTime;

            if (_state == MonsterAIState.Chase)
                _defendChaseTimer += deltaTime;

            TryFindTarget();
            EvaluateTransitions();
            ExecuteState(deltaTime);
        }

        private void TryFindTarget()
        {
            if (_target != null) return;

            var players = Hotfix.GameSystems.Sys3C.Core.Combat.PhysicsRegistry.Instance.FindNearby(
                _self.position, _config.DetectRange, EntityType.Player);
            if (players.Count > 0)
                _target = players[0].Transform;
        }

        public void NotifyHit(DamageBlock damageData, Vector3 hitDirection)
        {
            if (_state == MonsterAIState.Death) return;

            _lastHitDirection = hitDirection;
            _lastKnockbackForce = damageData?.KnockbackForce ?? 0f;

            // Defend: front absorbs, behind interrupts with knockback
            if (_state == MonsterAIState.Defend && _defend != null)
            {
                float angle = Vector3.Angle(_self.forward, -hitDirection);
                if (angle < _config.DefendAngle * 0.5f)
                {
                    var ctx = BuildContext();
                    ctx.DefendBlockCount++;
                    _animator.SetTrigger(HASH_Hit);
                    return;
                }
            }

            _preHitState = _state == MonsterAIState.Hit ? _preHitState : _state;

            _movement.ApplyKnockback(hitDirection, _lastKnockbackForce);
            _movement.Stop();
            _stateTimer = _config.KnockbackDecay + 0.3f;
            TransitionTo(MonsterAIState.Hit);
            _animator.SetTrigger(HASH_Hit);
        }

        public void EnterDeath()
        {
            TransitionTo(MonsterAIState.Death);
            _animator.SetTrigger(HASH_Death);
        }

        private void EvaluateTransitions()
        {
            float distToTarget = Target != null
                ? Vector3.Distance(_self.position, Target.position)
                : float.MaxValue;

            if (_state != MonsterAIState.Defend
                && _state != MonsterAIState.Taunt
                && _state != MonsterAIState.Alert
                && _state != MonsterAIState.Hit
                && _state != MonsterAIState.Death)
            {
                if (_defend != null && _defend.CanEnter(BuildContext()))
                {
                    TransitionTo(MonsterAIState.Defend);
                    return;
                }
            }

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
                    if (_stateTimer <= 0)
                    {
                        if (_taunt != null && _taunt.CanEnter(BuildContext()))
                            TransitionTo(MonsterAIState.Taunt);
                        else
                            TransitionTo(MonsterAIState.Chase);
                    }
                    break;

                case MonsterAIState.Defend:
                    if (_stateTimer <= 0)
                    {
                        if (_defend != null && _defend.IsCounterReady)
                            TransitionTo(MonsterAIState.Attack);
                        else if (distToTarget < _config.AttackRange && _attackCooldown <= 0)
                            TransitionTo(MonsterAIState.Attack);
                        else
                            TransitionTo(MonsterAIState.Chase);
                    }
                    break;

                case MonsterAIState.Taunt:
                    if (_stateTimer <= 0)
                    {
                        if (distToTarget < _config.AttackRange)
                            TransitionTo(MonsterAIState.Attack);
                        else if (Target != null)
                            TransitionTo(MonsterAIState.Chase);
                        else
                            TransitionTo(MonsterAIState.Idle);
                    }
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
                case MonsterAIState.Idle:
                    _movement.Stop();
                    break;
                case MonsterAIState.Patrol:
                    break;
                case MonsterAIState.Chase:
                    if (Target != null)
                    {
                        _movement.Chase(Target);
                        _movement.LookAt(Target.position);
                        _animator.SetFloat(HASH_Speed, _config.ChaseAnimIsRun ? 2f : 1f);
                    }
                    break;
                case MonsterAIState.Attack:
                    _movement.Stop();
                    if (Target != null)
                        _movement.LookAt(Target.position);
                    break;
                case MonsterAIState.Defend:
                    break;
                case MonsterAIState.Taunt:
                    break;
                case MonsterAIState.Hit:
                    _movement.UpdateKnockback(deltaTime);
                    break;
            }
        }

        private void TransitionTo(MonsterAIState newState)
        {
            if (_state == newState) return;

            ExitBehaviourForState(_state);

            var old = _state;
            _state = newState;
            _animator.SetInteger(HASH_AIState, (int)newState);
            OnStateChanged?.Invoke(old, newState);

            switch (newState)
            {
                case MonsterAIState.Idle:
                    _stateTimer = _idleDuration;
                    _animator.SetFloat(HASH_Speed, 0);
                    break;

                case MonsterAIState.Patrol:
                    _movement.PatrolTo(_patrolPoints[_patrolIndex]);
                    _patrolIndex = (_patrolIndex + 1) % _patrolPoints.Count;
                    _animator.SetFloat(HASH_Speed, 1f);
                    break;

                case MonsterAIState.Chase:
                    _defendChaseTimer = 0;
                    _movement.Resume();
                    _animator.SetFloat(HASH_Speed, 2f);
                    break;

                case MonsterAIState.Attack:
                    _attackCooldown = _attackCooldownBase;
                    _stateTimer = 0.5f;
                    _currentAttackIndex = PickAttackIndex();
                    _animator.SetInteger(HASH_AttackIndex, _currentAttackIndex);
                    _animator.SetTrigger(HASH_Attack);

                    var (damage, effect) = GetCurrentEffect();
                    _attackHitTarget = ResolveAttack(damage, effect);
                    OnAttackHitboxActivate?.Invoke(damage, effect);
                    break;

                case MonsterAIState.Defend:
                    _stateTimer = _config.DefendDuration;
                    EnterBehaviourForState(MonsterAIState.Defend);
                    break;

                case MonsterAIState.Taunt:
                    _stateTimer = _config.TauntDuration;
                    EnterBehaviourForState(MonsterAIState.Taunt);
                    break;

                case MonsterAIState.Alert:
                    EnterBehaviourForState(MonsterAIState.Alert);
                    break;
            }
        }

        private bool ResolveAttack(DamageBlock damage, EffectBlock effect)
        {
            if (damage == null) return false;
            int mask = LayerMask.GetMask("Character");
            var shape = AttackShapeFactory.Create(_config.AttackShape, PhysicsRegistry.Instance, EntityType.Player);
            _hitBuffer.Clear();
            shape.ResolveNonAlloc(_self.position, _self.forward, mask, _hitBuffer);
            foreach (var t in _hitBuffer)
            {
                Vector3 dir = (t.Transform.position - _self.position).normalized;
                t.TakeDamage(damage, dir);
            }
            return _hitBuffer.Count > 0;
        }

        private int PickAttackIndex()
        {
            if (_config.AttackAnimCount <= 1) return 0;
            if (_config.AttackWeights == null || _config.AttackWeights.Length == 0) return 0;
            float roll = UnityEngine.Random.value;
            float cumulative = 0;
            for (int i = 0; i < _config.AttackWeights.Length && i < _config.AttackAnimCount; i++)
            {
                cumulative += _config.AttackWeights[i];
                if (roll <= cumulative) return i;
            }
            return 0;
        }

        private (DamageBlock damage, EffectBlock effect) GetCurrentEffect()
        {
            var damage = _config.AttackDamage ?? DamageBlock.CreateDefault(_config.AttackPower);
            return (damage, _config.AttackEffect);
        }

        private MonsterAIContext BuildContext()
        {
            return new MonsterAIContext
            {
                Self = _self,
                Target = _target,
                Animator = _animator,
                Stats = _stats,
                Movement = _movement,
                Config = _config,
                DeltaTime = Time.deltaTime,
                StateTimer = _stateTimer,
                CurrentAttackIndex = _currentAttackIndex,
                AttackHitTarget = _attackHitTarget,
                AttackShape = _attackShape,
                DefendBlockCount = 0,
                DefendChaseTimer = _defendChaseTimer,
            };
        }

        private void EnterBehaviourForState(MonsterAIState state)
        {
            var ctx = BuildContext();
            if (state == MonsterAIState.Defend) _defend?.Enter(ctx);
            if (state == MonsterAIState.Taunt) _taunt?.Enter(ctx);
            if (state == MonsterAIState.Alert) _alert?.Enter(ctx);
        }

        private void ExitBehaviourForState(MonsterAIState state)
        {
            var ctx = BuildContext();
            if (state == MonsterAIState.Defend) _defend?.Exit(ctx);
            if (state == MonsterAIState.Taunt) _taunt?.Exit(ctx);
            if (state == MonsterAIState.Alert) _alert?.Exit(ctx);
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
            _target = null;
            _movement.ReturnToSpawn(_spawnPoint);
            TransitionTo(MonsterAIState.Idle);
        }

        private void GeneratePatrolPoints()
        {
            _patrolPoints.Clear();
            if (_patrolRadius <= 0) return;
            for (int i = 0; i < 3; i++)
            {
                float angle = (360f / 3) * i * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * _patrolRadius;
                _patrolPoints.Add(_spawnPoint + offset);
            }
        }
    }
}

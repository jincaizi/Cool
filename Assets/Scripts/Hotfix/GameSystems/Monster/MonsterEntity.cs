using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Combat;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterEntity : MonoBehaviour, IDamageable
    {
        [Header("Components")]
        public Animator Animator;
        public NavMeshAgent NavAgent;
        public HitZone HitZone;
        public AttackHitbox AttackHitbox;

        private MonsterConfig _config;
        private MonsterStats _stats;
        private MonsterAI _ai;
        private MonsterMovement _movement;
        private Vector3 _spawnPoint;

        bool IDamageable.IsAlive => !_stats.IsDead;
        Transform IDamageable.Transform => transform;

        public event Action OnDeathComplete;
        public event Action<LootResult[]> OnLootDrop;

        private void Awake()
        {
            if (Animator == null) Animator = GetComponent<Animator>();
            if (NavAgent == null) NavAgent = GetComponent<NavMeshAgent>();
            if (HitZone == null) HitZone = GetComponent<HitZone>();
            if (AttackHitbox == null) AttackHitbox = GetComponentInChildren<AttackHitbox>();
        }

        public void Init(MonsterConfig config, Vector3 spawnPoint)
        {
            _config = config;
            _spawnPoint = spawnPoint;

            PhysicsRegistry.Instance.Register(transform, EntityType.Monster);

            _stats = new MonsterStats(config);
            _movement = new MonsterMovement(NavAgent, transform, config);
            _ai = new MonsterAI(_movement, _stats, Animator, transform, config, spawnPoint);

            if (HitZone != null) HitZone.Init(this);

            _stats.OnDeath += HandleDeath;
            _stats.OnHPChanged += (cur, max) => { };

            _ai.OnDeathComplete += () => StartCoroutine(DeathSequence());
            _ai.OnAttackHitboxActivate += (effect) =>
            {
                if (AttackHitbox != null && effect != null)
                {
                    AttackHitbox.Activate(effect);
                    HitZone.ResetHits();
                }
            };
            _ai.OnAttackHitboxDeactivate += () =>
            {
                if (AttackHitbox != null)
                    AttackHitbox.Deactivate();
            };
        }

        private void Update()
        {
            if (_stats == null || _stats.IsDead || _ai == null) return;
            _ai.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            PhysicsRegistry.Instance.Unregister(transform);
        }

        void IDamageable.TakeDamage(DamageData data, Vector3 hitDirection)
        {
            if (_stats.IsDead) return;
            _stats.TakeDamage(data);
            _ai.NotifyHit(data, hitDirection);
        }

        private void HandleDeath()
        {
            _ai.EnterDeath();
            if (NavAgent != null) NavAgent.enabled = false;
        }

        private IEnumerator DeathSequence()
        {
            var loot = _config.LootTable?.Roll();
            if (loot != null && loot.Count > 0)
            {
                var lootArr = loot.ToArray();
                OnLootDrop?.Invoke(lootArr);
                EventBus.Emit(new MonsterDeathEvent(_config.MonsterId, transform.position, lootArr));
            }

            yield return new WaitForSeconds(_config.DeathDestroyDelay);
            OnDeathComplete?.Invoke();
            Destroy(gameObject);
        }
    }
}

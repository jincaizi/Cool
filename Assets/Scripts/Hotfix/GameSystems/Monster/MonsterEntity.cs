using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Hotfix.GameSystems.Combat;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterEntity : MonoBehaviour, IDamageable
    {
        [Header("Components")]
        public Animator Animator;
        public NavMeshAgent NavAgent;
        public MonsterHitZone HitZone;
        public MonsterAttackHitbox AttackHitbox;

        private MonsterConfig _config;
        private MonsterStats _stats;
        private MonsterAI _ai;
        private MonsterMovement _movement;
        private Vector3 _spawnPoint;

        bool IDamageable.IsAlive => !_stats.IsDead;
        Transform IDamageable.Transform => transform;

        public event Action OnDeathComplete;
        public event Action<LootResult[]> OnLootDrop;

        public void Init(MonsterConfig config, Vector3 spawnPoint)
        {
            _config = config;
            _spawnPoint = spawnPoint;

            _stats = new MonsterStats(config);
            _movement = new MonsterMovement(NavAgent, transform, config);
            _ai = new MonsterAI(_movement, _stats, Animator, transform, config, spawnPoint);

            HitZone.Init(this);

            _stats.OnDeath += HandleDeath;
            _stats.OnHPChanged += (cur, max) => { /* HUD update hook */ };

            _ai.OnDeathComplete += () => StartCoroutine(DeathSequence());
            _ai.OnAttackFrame += () =>
            {
                if (AttackHitbox != null && config.AttackDamage != null)
                    AttackHitbox.Activate(config.AttackDamage);
            };
        }

        public void TakeDamageFromHitbox(AttackHitboxData hitboxData, Vector3 hitDirection)
        {
            if (_stats.IsDead || hitboxData == null || hitboxData.DamageData == null) return;

            _stats.TakeDamage(hitboxData.DamageData);
            _ai.NotifyHit(hitboxData.DamageData, hitDirection);
        }

        private void Update()
        {
            if (_stats == null || _stats.IsDead) return;
            _ai.Update(Time.deltaTime);
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
            NavAgent.enabled = false;
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

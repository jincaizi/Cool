using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Nameplate;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterEntity : MonoBehaviour, IDamageable, ITargetable
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

            PhysicsRegistry.Instance.Register(this, EntityType.Monster);

            _stats = new MonsterStats(config);
            _movement = new MonsterMovement(NavAgent, transform, config);
            _ai = new MonsterAI(_movement, _stats, Animator, transform, config, spawnPoint);

            if (HitZone != null) HitZone.Init(this);

            _stats.OnDeath += HandleDeath;
            _stats.OnHPChanged += (cur, max) =>
            {
                _onHPChanged?.Invoke(
                    max > 0 ? cur / max : 0f,
                    Mathf.CeilToInt(cur),
                    Mathf.CeilToInt(max));
            };
            _stats.OnDeath += () => _onDeath?.Invoke();

            _ai.OnDeathComplete += () => StartCoroutine(DeathSequence());
            _ai.OnAttackHitboxActivate += (damage, effect) =>
            {
                if (AttackHitbox != null)
                {
                    AttackHitbox.Activate(damage, effect);
                    HitZone.ResetHits();
                }
            };
            _ai.OnAttackHitboxDeactivate += () =>
            {
                if (AttackHitbox != null)
                    AttackHitbox.Deactivate();
            };

            // Register nameplate
            var displayMgr = EntityDisplayManager.Instance;
            if (displayMgr != null && !string.IsNullOrEmpty(_config.DisplayName))
            {
                var cfg = new NameplateConfig(_config.DisplayName, ColorPalette.Monster);
                displayMgr.Register(GetInstanceID(), transform, cfg);
            }
        }

        private void Update()
        {
            if (_stats == null || _stats.IsDead || _ai == null) return;
            _ai.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            EntityDisplayManager.Instance?.Unregister(GetInstanceID());
            PhysicsRegistry.Instance.Unregister(this);
        }

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
        {
            if (_stats.IsDead) return;
            _stats.TakeDamage(data);
            _ai.NotifyHit(data, hitDirection);

            // Emit monster damage event for floating text
            EventBus.Emit(new MonsterTakeDamageEvent(
                GetInstanceID(),
                transform.position + Vector3.up * 2f,
                Mathf.CeilToInt(data.BaseDamage),
                data.WasCritical
            ));
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

        // ===== ITargetable =====

        private event Action<float, int, int> _onHPChanged;
        private event Action _onDeath;

        event Action<float, int, int> ITargetable.OnHPChanged
        {
            add { _onHPChanged += value; }
            remove { _onHPChanged -= value; }
        }
        event Action ITargetable.OnDeath
        {
            add { _onDeath += value; }
            remove { _onDeath -= value; }
        }

        string ITargetable.DisplayName => _config != null ? _config.DisplayName : name;
        int ITargetable.Level => 1;
        Sprite ITargetable.Portrait => null;
        float ITargetable.HPPercent => _stats != null ? _stats.HP / _stats.MaxHP : 0f;
        int ITargetable.CurrentHP => _stats != null ? Mathf.CeilToInt(_stats.HP) : 0;
        int ITargetable.MaxHP => _stats != null ? Mathf.CeilToInt(_stats.MaxHP) : 0;
        Vector3 ITargetable.WorldPosition => transform.position;
    }
}

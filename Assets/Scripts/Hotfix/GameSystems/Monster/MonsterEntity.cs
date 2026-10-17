using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Nameplate;

namespace Hotfix.GameSystems.Monster
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class MonsterEntity : MonoBehaviour, IDamageable, ITargetable, IEffectTarget
    {
        [Header("Components")]
        public Animator Animator;
        public NavMeshAgent NavAgent;
        public HitZone HitZone;
        public AttackHitbox AttackHitbox;

        private MonsterConfig _config;
        public MonsterConfig Config => _config;
        private MonsterStats _stats;
        private MonsterMovement _movement;
        private DamagePipeline _damagePipeline;
        private AIBrain _brain;
        private AIContext _ctx;
        private Vector3 _spawnPoint;
        private Collider _collider;

        // _stats 由 Init 建立：npc 视图（联网模式的怪物表现）从不调 Init，所以这里必须判空 ——
        // 否则视图会被判成「活着」而选进目标集合。同文件 ITargetable 的 HP 系列本来就在判空。
        bool IDamageable.IsAlive => _stats != null && !_stats.IsDead;
        Transform IDamageable.Transform => transform;

        public event Action OnDeathComplete;
        public event Action<LootResult[]> OnLootDrop;

        private void Awake()
        {
            if (Animator == null) Animator = GetComponent<Animator>();
            if (NavAgent == null) NavAgent = GetComponent<NavMeshAgent>();
            if (HitZone == null) HitZone = GetComponent<HitZone>();
            if (_collider == null) _collider = GetComponent<Collider>();
            if (AttackHitbox == null) AttackHitbox = GetComponentInChildren<AttackHitbox>();
        }

        private void OnEnable()
        {
            EventBus.SubscribeTargeted<KnockbackEvent>(GetInstanceID(), OnKnockback);
        }

        private void OnDisable()
        {
            EventBus.UnsubscribeTargeted<KnockbackEvent>(GetInstanceID(), OnKnockback);
        }

        private void OnKnockback(KnockbackEvent e)
        {
            if (_stats == null || _stats.IsDead) return;
            _movement.ApplyKnockback(e.Direction, e.Force);
        }

        public void Init(MonsterConfig config, Vector3 spawnPoint)
        {
            // P6 ⑤：池化复用的对象会带着上一轮的订阅——必须清空，
            // 否则 MonsterSpawner 每次生成 += 会累积成多处理器。
            OnDeathComplete = null;

            _config = config;
            _spawnPoint = spawnPoint;

            PhysicsRegistry.Instance.Register(this, EntityType.Monster);
            EnsurePhysicsCollider();

            // Build systems bottom-up
            _stats = new MonsterStats(config);
            _movement = new MonsterMovement(NavAgent, transform, config);

            // Shared AI context — class, mutations persist across calls
            _ctx = new AIContext
            {
                Self = transform,
                Animator = Animator,
                Stats = _stats,
                Movement = _movement,
                Config = config,
            };

            // Damage pipeline: i-frame modifier (built-in) + defend modifier (only active in DefendState)
            _damagePipeline = new DamagePipeline(config, _stats);
            var defendCfg = new DefendConfig
            {
                DamageReduction = config.DefendDamageReduction,
                DefendAngle = config.DefendAngle
            };
            _damagePipeline.AddModifier(new DefendModifier(defendCfg, transform, () => _ctx.CurrentState == MonsterAIState.Defend));

            // Build AI states
            var patrolState = new PatrolState();
            var patrolRadius = RandomRange(config.PatrolRadius, config.PatrolRadiusVariance);
            patrolState.GeneratePatrolPoints(spawnPoint, patrolRadius);

            var hitState = new HitState(_damagePipeline);

            var states = new Dictionary<MonsterAIState, AIStateBase>
            {
                { MonsterAIState.Idle, new IdleState() },
                { MonsterAIState.Patrol, patrolState },
                { MonsterAIState.Chase, new ChaseState() },
                { MonsterAIState.Attack, new MonsterAttackState() },
                { MonsterAIState.Hit, hitState },
                { MonsterAIState.Death, new DeathState() },
                { MonsterAIState.Defend, new DefendState() },
                { MonsterAIState.Taunt, new TauntState() },
            };

            var fsm = new AIStateMachine(states, MonsterAIState.Idle);
            _brain = new AIBrain(_ctx, fsm, config);
            _brain.Initialize();

            // Wire hit zone
            if (HitZone != null) HitZone.Init(this);

            // Wire stats events to external systems (nameplate, target panel)
            _stats.OnHPChanged += (cur, max) =>
            {
                _onHPChanged?.Invoke(
                    max > 0 ? cur / max : 0f,
                    Mathf.CeilToInt(cur),
                    Mathf.CeilToInt(max));
            };
            _stats.OnDeath += () =>
            {
                _onDeath?.Invoke();
                HandleDeath();
            };

            // Register nameplate
            var displayMgr = EntityDisplayManager.Instance;
            if (displayMgr != null && !string.IsNullOrEmpty(_config.DisplayName))
            {
                var cfg = _config.NameplateData != null
                    ? NameplateConfig.FromData(_config.NameplateData, _config.DisplayName)
                    : new NameplateConfig(_config.DisplayName);
                displayMgr.Register(GetInstanceID(), transform, cfg);
            }
        }

        private void Update()
        {
            if (_stats == null || _stats.IsDead || _brain == null) return;
            _brain.Update(Time.deltaTime);
        }

        // Safety net: unsubscribe even if OnDisable is skipped (e.g., DestroyImmediate in editor)
        private void OnDestroy()
        {
            EventBus.UnsubscribeTargeted<KnockbackEvent>(GetInstanceID(), OnKnockback);
            EntityDisplayManager.Instance?.Unregister(GetInstanceID());
            PhysicsRegistry.Instance?.Unregister(this);
        }

        // ── Damage Pipeline ──

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
            => ((IDamageable)this).TakeDamage(data, hitDirection, HitContact.None);

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection, HitContact contact)
        {
            // 未 Init（npc 视图）⇒ 本轮伤害与它无关，安静返回。**这条守卫不能省**：视图的剥离是
            // 「先禁再销毁」，销毁帧末才生效，而 `enabled = false` 挡不住接口调用 —— 创建帧内
            // 仍可能被同帧判定命中（与 StripCollider 注释里那条窗口同类）。
            if (_stats == null || _stats.IsDead) return;

            // None → 方向重建（无碰撞箱路径也能拿真实部位）
            HitContact resolved = contact.HasContact || _collider == null
                ? contact
                : HitContact.FromDirection(_collider, hitDirection);

            int myId = GetInstanceID();

            var ctx = new DamageContext
            {
                RawData = data,
                HitDirection = hitDirection,
                AttackerId = 0,
                Flags = data.WasCritical ? DamageFlags.IsCritical : DamageFlags.None,
                OverrideDamage = data.CalculatedDamage,
                Contact = resolved,
            };

            var result = _damagePipeline.Process(ref ctx);

            _brain.OnDamageReceived(result, hitDirection);

            EmitDamageEvents(myId, data, hitDirection, result, resolved);

            if (result.ShouldKnockback && data.KnockbackForce > 0)
            {
                float force = data.KnockbackForce * HitZoneClassifier.KnockbackMultiplier(resolved.Part);
                EventBus.TargetedEmit(myId, new KnockbackEvent(myId, hitDirection, force));
            }
        }

        private void EmitDamageEvents(int entityId, DamageBlock data, Vector3 hitDirection,
            DamageResult result, HitContact contact)
        {
            // Blocked hits don't show float text
            if (result.WasBlocked) return;

            var displayDamage = Mathf.CeilToInt(result.FinalDamage);
            var damageEvent = new MonsterTakeDamageEvent(
                entityId,
                contact.HitPoint,
                hitDirection,
                displayDamage,
                data.WasCritical,
                data.SkillId,
                data.ComboIndex,
                contact.Part,
                contact.ElementColor
            );
            EventBus.Emit(damageEvent);
            EventBus.TargetedEmit(entityId, damageEvent);
        }


        private void HandleDeath()
        {
            if (_movement != null)
            {
                // Apply death knockback with configurable multiplier
                var dir = _brain.LastHitDirection;
                var force = _brain.LastKnockbackForce * _config.DeathKnockbackMultiplier;
                _movement.ApplyKnockback(dir, force);
                _movement.Stop();
            }

            _brain.EnterDeath();
            if (NavAgent != null) NavAgent.enabled = false;

            StartCoroutine(DeathSequence());
        }

        private IEnumerator DeathSequence()
        {
            // Brief pause for death animation to play
            yield return new WaitForSeconds(0.5f);

            // Roll loot table
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

        // ── Physics Collider ──

        private void EnsurePhysicsCollider()
        {
            var triggerCol = GetComponent<Collider>();
            if (triggerCol == null) return;

            var allColliders = GetComponents<Collider>();
            foreach (var c in allColliders)
            {
                if (!c.isTrigger) return;
            }

            if (triggerCol is CapsuleCollider capsule)
            {
                var physicsCol = gameObject.AddComponent<CapsuleCollider>();
                physicsCol.center = capsule.center;
                physicsCol.radius = capsule.radius;
                physicsCol.height = capsule.height;
                physicsCol.isTrigger = false;
            }
            else if (triggerCol is SphereCollider sphere)
            {
                var physicsCol = gameObject.AddComponent<SphereCollider>();
                physicsCol.center = sphere.center;
                physicsCol.radius = sphere.radius;
                physicsCol.isTrigger = false;
            }
            else if (triggerCol is BoxCollider box)
            {
                var physicsCol = gameObject.AddComponent<BoxCollider>();
                physicsCol.center = box.center;
                physicsCol.size = box.size;
                physicsCol.isTrigger = false;
            }
        }

        // ── Utility ──

        private static float RandomRange(float baseValue, float variance)
        {
            if (variance <= 0) return baseValue;
            return baseValue + UnityEngine.Random.Range(-variance, variance);
        }

        // ── ITargetable ──

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
        float ITargetable.SelectionRingYOffset => _config?.RingYOffset ?? 0f;

        // ── IEffectTarget ──

        IEffectStats IEffectTarget.Stats => null;
        IShieldSystem IEffectTarget.ShieldSystem => null;
        IPhysicsSystem IEffectTarget.PhysicsSystem => null;
        IStatusController IEffectTarget.StatusController => null;

        void IEffectTarget.Heal(float amount)
        {
            // Negative amount = damage. Route through pipeline for consistency.
            if (amount >= 0 || _stats == null || _stats.IsDead) return;

            float damage = -amount;
            var damageBlock = DamageBlock.CreateDefault(damage);
            ((IDamageable)this).TakeDamage(damageBlock, Vector3.zero);
        }
    }
}

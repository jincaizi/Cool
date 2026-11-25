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
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Nameplate;
using Cool.SyncCore;

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

        /// <summary>受击体积半径（XZ，米）；单机剑气回荡等表面距离选敌使用。</summary>
        public float BodyRadius { get; private set; }
        private MonsterStats _stats;
        private MonsterMovement _movement;
        private DamagePipeline _damagePipeline;
        private AIBrain _brain;
        private AIContext _ctx;
        private Vector3 _spawnPoint;
        private Collider _collider;

        private readonly StatusControllerAdapter _statusController = new();
        private OfflineMonsterStatuses _elementalStatuses;
        private uint _life;
        private bool _wasStunned;
        private bool IsStunned => _statusController.IsStunned || _elementalStatuses?.ActionLock == true;

        // _stats 由 Init 建立：npc 视图（联网模式的怪物表现）从不调 Init，所以这里必须判空 ——
        // 否则视图会被判成「活着」而选进目标集合。同文件 ITargetable 的 HP 系列本来就在判空。
        bool IDamageable.IsAlive => this != null && isActiveAndEnabled && _stats != null && !_stats.IsDead;
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
            if (_collider != null)
            {
                var extents = _collider.bounds.extents;
                BodyRadius = Mathf.Max(extents.x, extents.z);
            }
        }

        private void OnEnable()
        {
            EventBus.SubscribeTargeted<KnockbackEvent>(GetInstanceID(), OnKnockback);
        }

        private void OnDisable()
        {
            EventBus.UnsubscribeTargeted<KnockbackEvent>(GetInstanceID(), OnKnockback);
            ClearElementalStatuses();
        }

        private void OnKnockback(KnockbackEvent e)
        {
            if (_stats == null || _stats.IsDead) return;
            if (IsStunned || _ctx?.HasArmoredWindup == true) return;
            _movement.ApplyKnockback(e.Direction, e.Force, e.FollowNavigation);
        }

        public void Init(MonsterConfig config, Vector3 spawnPoint)
        {
            // P6 ⑤：池化复用的对象会带着上一轮的订阅——必须清空，
            // 否则 MonsterSpawner 每次生成 += 会累积成多处理器。
            OnDeathComplete = null;
            ClearElementalStatuses();
            ++_life;

            _config = config;
            _spawnPoint = spawnPoint;

            // 战斗形态切到 AI 控制器；Npc 视图不调 Init，继续沿用预制体上的 NpcAnimator_*。
            if (Animator != null && config.CombatAnimatorController != null
                && Animator.runtimeAnimatorController != config.CombatAnimatorController)
            {
                Animator.runtimeAnimatorController = config.CombatAnimatorController;
            }

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

            _elementalStatuses?.AdvanceTo(OfflineMonsterStatuses.LocalTick);
            if (_stats.IsDead) return;
            SyncStatusControl();

            if (!IsStunned)
                _brain.Update(Time.deltaTime);
        }

        private void SyncStatusControl()
        {
            _movement?.SetStatusSpeedScale(_elementalStatuses?.SpeedScale ?? 1f);
            bool stunned = IsStunned;
            if (stunned != _wasStunned)
            {
                if (Animator != null)
                    Animator.speed = stunned ? 0f : 1f;

                _movement?.SetActionLock(stunned);
                _wasStunned = stunned;
            }
        }

        private void ClearElementalStatuses()
        {
            if (_elementalStatuses == null) return;
            _elementalStatuses.Clear();
            SyncStatusControl();
        }

        // Safety net: unsubscribe even if OnDisable is skipped (e.g., DestroyImmediate in editor)
        private void OnDestroy()
        {
            ClearElementalStatuses();
            EventBus.UnsubscribeTargeted<KnockbackEvent>(GetInstanceID(), OnKnockback);
            EntityDisplayManager.Instance?.Unregister(GetInstanceID());
            PhysicsRegistry.Instance?.Unregister(this);
        }

        // ── Damage Pipeline ──

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
            => ((IDamageable)this).TakeDamage(data, hitDirection, HitContact.None);

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection, HitContact contact)
            => TakeDamage(data, hitDirection, contact, null);

        internal void TakeOfflineWeaponDamage(DamageBlock data, Vector3 hitDirection, OfflineWeaponCast cast)
            => TakeDamage(data, hitDirection, HitContact.None, cast);

        private void TakeDamage(DamageBlock data, Vector3 hitDirection, HitContact contact,
            OfflineWeaponCast weaponCast)
        {
            // 未 Init（npc 视图）⇒ 本轮伤害与它无关，安静返回。**这条守卫不能省**：视图的剥离是
            // 「先禁再销毁」，销毁帧末才生效，而 `enabled = false` 挡不住接口调用 —— 创建帧内
            // 仍可能被同帧判定命中（与 StripCollider 注释里那条窗口同类）。
            if (this == null || !isActiveAndEnabled || _stats == null || _stats.IsDead) return;
            uint tick = OfflineMonsterStatuses.LocalTick;
            _elementalStatuses?.AdvanceTo(tick);
            if (_stats.IsDead) return;
            SyncStatusControl();
            bool armoredWindup = _ctx?.HasArmoredWindup == true;

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

            var result = _damagePipeline.Process(ref ctx, _elementalStatuses?.IncomingDamageMultiplier ?? 1f);

            if (!IsStunned && !armoredWindup)
                _brain?.OnDamageReceived(result, hitDirection);

            EmitDamageEvents(myId, data, hitDirection, result, resolved);

            if (!armoredWindup && result.ShouldKnockback && data.KnockbackForce > 0)
            {
                float force = data.KnockbackForce * HitZoneClassifier.KnockbackMultiplier(resolved.Part);
                EventBus.TargetedEmit(myId, new KnockbackEvent(myId, hitDirection, force,
                    followNavigation: data.SkillId == CombatRules.ChargeBlastSkillId));
            }

            // 部分格挡仍扣血，但同样拒绝元素附加。只有首个有效存活命中占用该施法的附加名额。
            if (weaponCast == null || result.WasBlocked || ctx.BlockCount > 0
                || !(result.FinalDamage > 0f) || _stats.IsDead)
                return;
            var element = weaponCast.ClaimElement(myId, _life);
            if (element == null) return;
            _elementalStatuses ??= new OfflineMonsterStatuses(transform, myId, ApplyDotDamage);
            _elementalStatuses.Apply(element, weaponCast, tick, armoredWindup);
            SyncStatusControl();
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
            ClearElementalStatuses();
            if (Animator != null) Animator.speed = 1f;
            _wasStunned = false;

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
        IStatusController IEffectTarget.StatusController => _statusController;

        void IEffectTarget.Heal(float amount)
        {
            // Negative amount = damage. Route through pipeline for consistency.
            if (amount >= 0 || _stats == null || _stats.IsDead) return;

            float damage = -amount;
            var damageBlock = DamageBlock.CreateDefault(damage);
            ((IDamageable)this).TakeDamage(damageBlock, Vector3.zero);
        }

        void IEffectTarget.ApplyDamageOverTime(float amount)
        {
            _elementalStatuses?.AdvanceTo(OfflineMonsterStatuses.LocalTick);
            ApplyDotDamage(amount * (_elementalStatuses?.IncomingDamageMultiplier ?? 1f));
        }

        private void ApplyDotDamage(float amount)
        {
            if (this == null || !isActiveAndEnabled || _stats == null || _stats.IsDead || amount <= 0f) return;

            _stats.ApplyDamage(amount);          // 纯扣血，绕过防御

            EventBus.Emit(new MonsterDotDamageEvent(
                GetInstanceID(),
                transform.position + Vector3.up * 1.2f,
                Mathf.CeilToInt(amount)));
        }
    }
}

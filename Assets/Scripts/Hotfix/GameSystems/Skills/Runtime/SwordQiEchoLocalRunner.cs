using System;
using System.Collections.Generic;
using Cool.SyncCore;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.VFX;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 单机「剑气回荡」本地执行：共享规则选敌/时间表 → 协调器启动执行器 →
    /// 到接触 tick 走本地 IDamageable；施法窗口内本地受击入口免伤。
    /// </summary>
    public sealed class SwordQiEchoLocalRunner
    {
        private sealed class RuntimeContact
        {
            public long TargetId;
            public MonsterEntity Target = null!;
            public uint OffsetTicks;
            public bool Resolved;
        }

        private readonly SkillCoordinator _coordinator;
        private readonly Transform _owner;
        private readonly Func<float> _critRoll;
        private readonly Func<double> _now;
        private readonly List<RuntimeContact> _contacts = new List<RuntimeContact>();
        private Vec3 _origin;
        private double _startTime;
        private double _endTime;
        private bool _active;
        private SwordQiEchoSkillData? _prepared;
        private SwordQiEchoPresenter? _presenter;
        private GameObject? _contactVfx;

        public bool IsInvulnerable => _active && _now() < _endTime;

        public SwordQiEchoLocalRunner(SkillCoordinator coordinator, Transform owner,
            Func<float>? critRoll = null, Func<double>? now = null)
        {
            _coordinator = coordinator;
            _owner = owner;
            _critRoll = critRoll ?? (() => UnityEngine.Random.value);
            _now = now ?? (() => Time.timeAsDouble);
        }

        /// <summary>第一次布剑，第二次按当前位置选敌。无目标时保留剩余待发时间。</summary>
        public bool TryCast(SwordQiEchoSkillData data)
        {
            if (data == null || _active) return false;
            if (_prepared == null)
            {
                _prepared = data;
                _coordinator.SetEchoPreparation(1, SwordQiEchoRules.PrepareSeconds, 0d, _now);
                if (_presenter == null)
                    _presenter = _owner.GetComponent<SwordQiEchoPresenter>();
                if (_presenter == null)
                    _presenter = _owner.gameObject.AddComponent<SwordQiEchoPresenter>();
                _presenter.Prepare(_owner, SwordQiEchoRules.Spec.SwordCount, SwordQiEchoRules.PrepareSeconds,
                    now: _now, swordPrefab: data.SwordVfx);
                return true;
            }
            if (ExpirePreparation()) return false;

            var spawner = MonsterSpawner.Active;
            if (spawner == null)
            {
                _coordinator.NotifyEchoNoTarget();
                return false;
            }

            var origin = new Vec3(_owner.position.x, _owner.position.y, _owner.position.z);
            var monsters = spawner.SnapshotAlive();
            var candidates = new List<CombatCandidate>(monsters.Count);
            var byId = new Dictionary<long, MonsterEntity>(monsters.Count);
            foreach (var monster in monsters)
            {
                if (monster == null) continue;
                long id = monster.GetInstanceID();
                var position = monster.transform.position;
                candidates.Add(new CombatCandidate(id,
                    new Vec3(position.x, position.y, position.z), true, monster.BodyRadius));
                byId[id] = monster;
            }

            var targets = SwordQiEchoRules.SelectTargets(origin, SwordQiEchoRules.Spec.Radius,
                SwordQiEchoRules.Spec.MaxTargets, candidates);
            if (targets.Count == 0)
            {
                _coordinator.NotifyEchoNoTarget();
                return false;
            }

            double now = _now();
            double endTime = now + SwordQiEchoRules.Spec.DurationSeconds;
            if (!_coordinator.StartOfflineEcho(data, endTime, _now))
                return false;
            _prepared = null;
            _coordinator.ClearEchoPreparation();

            uint durationTicks = (uint)Math.Ceiling(
                SwordQiEchoRules.Spec.DurationSeconds * CombatRules.TickRateHz);
            var scheduled = SwordQiEchoRules.BuildContacts(origin, targets,
                SwordQiEchoRules.Spec.SwordCount, durationTicks, (ulong)UnityEngine.Random.Range(1, int.MaxValue));

            _contacts.Clear();
            foreach (var contact in scheduled)
            {
                if (!byId.TryGetValue(contact.TargetId, out var target)) continue;
                _contacts.Add(new RuntimeContact
                {
                    TargetId = contact.TargetId,
                    Target = target,
                    OffsetTicks = contact.OffsetTicks
                });
            }
            _origin = origin;
            _startTime = now;
            _endTime = endTime;
            _active = true;

            _contactVfx = data.ContactVfx;
            _presenter?.Launch(_owner, scheduled, SwordQiEchoRules.Spec.DurationSeconds,
                SwordQiEchoRules.Spec.SwordCount, now: _now, swordPrefab: data.SwordVfx);
            return true;
        }

        public void Update()
        {
            ExpirePreparation();
            if (!_active) return;
            double now = _now();

            foreach (var contact in _contacts)
            {
                if (contact.Resolved) continue;
                double due = _startTime + contact.OffsetTicks / (double)CombatRules.TickRateHz;
                if (now < due) continue;
                contact.Resolved = true;

                var target = contact.Target;
                if (target == null || !((IDamageable)target).IsAlive) continue;

                var position = target.transform.position;
                var targetPos = new Vec3(position.x, position.y, position.z);
                if (HitResolver.SurfaceDistance(_origin, targetPos, target.BodyRadius)
                    > SwordQiEchoRules.Spec.Radius) continue;

                float baseDamage = SwordQiEchoRules.DefaultDamage;
                float criticalRate = CombatRules.TryGet((int)SkillID.SwordQiEcho, out var entry)
                    ? entry.CriticalRate
                    : 0f;
                bool critical = _critRoll() < criticalRate;
                float damage = baseDamage * (critical ? CombatRules.CritMultiplier : 1f);

                var block = DamageBlock.CreateDefault(damage);
                block.CalculatedDamage = damage;
                block.SkillId = (int)SkillID.SwordQiEcho;

                Vector3 direction = position - new Vector3(_origin.X, _origin.Y, _origin.Z);
                if (direction.sqrMagnitude < 0.0001f) direction = _owner.forward;
                var targetable = (ITargetable)target;
                int hpBefore = targetable.CurrentHP;
                ((IDamageable)target).TakeDamage(block, direction.normalized, HitContact.None);
                if (target != null && targetable.CurrentHP < hpBefore)
                    SwordQiEchoPresenter.PlayContact(_contactVfx, position + Vector3.up * 1.05f);
            }
            if (now >= _endTime) _active = false;
        }

        private bool ExpirePreparation()
        {
            if (_prepared == null || _coordinator.GetEchoPreparation().Remaining > 0f) return false;
            var data = _prepared;
            _prepared = null;
            _coordinator.ClearEchoPreparation();
            _coordinator.ApplyAuthoritativeCooldown(data.SkillId, data.Cooldown, 0f);
            return true;
        }

        public void Cancel()
        {
            _active = false;
            _prepared = null;
            _coordinator.ClearEchoPreparation();
            _contacts.Clear();
            _contactVfx = null;
            _presenter?.Stop();
        }
    }
}

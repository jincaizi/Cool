using System;
using System.Collections.Generic;
using Cool.SyncCore;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Sys3C.Core.Combat;
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
        private readonly List<RuntimeContact> _contacts = new List<RuntimeContact>();
        private Vec3 _origin;
        private double _startTime;
        private double _endTime;
        private bool _active;

        public bool IsInvulnerable => _active && Time.time < _endTime;

        public SwordQiEchoLocalRunner(SkillCoordinator coordinator, Transform owner,
            Func<float>? critRoll = null)
        {
            _coordinator = coordinator;
            _owner = owner;
            _critRoll = critRoll ?? (() => UnityEngine.Random.value);
        }

        /// <summary>本地选敌；零目标返回 false（不冷却、不动画）。</summary>
        public bool TryCast(SwordQiEchoSkillData data)
        {
            if (data == null || _active) return false;
            var spawner = MonsterSpawner.Active;
            if (spawner == null) return false;

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
            if (targets.Count == 0) return false;

            double endTime = Time.time + SwordQiEchoRules.Spec.DurationSeconds;
            if (!_coordinator.StartOfflineEcho(data, endTime, () => (double)Time.time))
                return false;

            uint durationTicks = (uint)Math.Ceiling(
                SwordQiEchoRules.Spec.DurationSeconds * CombatRules.TickRateHz);
            var scheduled = SwordQiEchoRules.BuildContacts(origin, targets,
                SwordQiEchoRules.Spec.SwordCount, durationTicks);

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
            if (_contacts.Count == 0) return false;

            _origin = origin;
            _startTime = Time.time;
            _endTime = endTime;
            _active = true;
            return true;
        }

        public void Update()
        {
            if (!_active) return;
            double now = Time.time;
            if (now >= _endTime)
            {
                _active = false;
                return;
            }

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

                Vector3 direction = position - _owner.position;
                if (direction.sqrMagnitude < 0.0001f) direction = _owner.forward;
                ((IDamageable)target).TakeDamage(block, direction.normalized, HitContact.None);
            }
        }

        public void Cancel() => _active = false;
    }
}

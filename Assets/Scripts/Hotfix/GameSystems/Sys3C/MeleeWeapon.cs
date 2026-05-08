using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C
{
    public class MeleeWeapon : MonoBehaviour, IWeapon
    {
        [SerializeField] private WeaponConfig _config;
        private float _attackCooldownTimer;
        private readonly List<IDamageable> _hitBuffer = new List<IDamageable>(16);

        public WeaponConfig Config => _config;
        public WeaponType WeaponType => WeaponType.Melee;

        public bool CanAttack() => _attackCooldownTimer <= 0;

        public void Attack(Vector3 forward, LayerMask targetMask)
        {
            if (_config == null) return;

            var shape = AttackShapeFactory.Create(_config.AttackShape, PhysicsRegistry.Instance, EntityType.Monster);
            _hitBuffer.Clear();
            shape.ResolveNonAlloc(transform.position, forward, targetMask, _hitBuffer);

            if (_hitBuffer.Count == 0)
            {
                Debug.Log("[Attack] Miss - no target in range");
                return;
            }

            if (_config.Damage == null) return;

            foreach (var t in _hitBuffer)
            {
                Vector3 dir = (t.Transform.position - transform.position).normalized;
                t.TakeDamage(_config.Damage, dir);
                Debug.Log($"[Attack] Hit {t.Transform.name} for {_config.Damage.BaseDamage} damage");
            }

            _attackCooldownTimer = 1f / _config.AttackSpeed;
        }

        private void Update()
        {
            if (_attackCooldownTimer > 0)
                _attackCooldownTimer -= Time.deltaTime;
        }
    }
}

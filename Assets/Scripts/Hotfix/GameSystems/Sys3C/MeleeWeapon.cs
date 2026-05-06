using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C
{
    public class MeleeWeapon : MonoBehaviour, IWeapon
    {
        [SerializeField] private WeaponConfig _config;
        private float _attackCooldownTimer;

        public WeaponConfig Config => _config;
        public WeaponType WeaponType => WeaponType.Melee;

        public bool CanAttack() => _attackCooldownTimer <= 0;

        public void Attack(Vector3 forward, LayerMask targetMask)
        {
            if (_config == null) return;

            var shape = AttackShapeFactory.Create(_config.AttackShape);
            var targets = shape.Resolve(transform.position, forward, targetMask);

            if (targets.Count == 0)
            {
                Debug.Log("[Attack] Miss - no target in range");
                return;
            }

            if (_config.Effects == null || _config.Effects.Length == 0) return;

            foreach (var t in targets)
            {
                foreach (var e in _config.Effects)
                {
                    Vector3 dir = (t.Transform.position - transform.position).normalized;
                    t.TakeDamage(e.Damage, dir);
                    Debug.Log($"[Attack] Hit {t.Transform.name} for {e.Damage.BaseDamage} damage");
                }
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

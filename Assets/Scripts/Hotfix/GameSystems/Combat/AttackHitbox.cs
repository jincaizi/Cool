using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Combat
{
    public class AttackHitbox : MonoBehaviour
    {
        public bool IsActive { get; private set; }

        private AttackHitboxData _currentData;
        private readonly HashSet<IDamageable> _hitTargets = new();

        private void Awake()
        {
            gameObject.SetActive(false);
        }

        public void Activate(AttackHitboxData data)
        {
            _currentData = data;
            IsActive = true;
            _hitTargets.Clear();
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }

        private void OnTriggerStay(Collider other)
        {
            if (!IsActive) return;

            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null || _hitTargets.Contains(damageable)) return;

            _hitTargets.Add(damageable);
            Vector3 dir = (damageable.Transform.position - transform.position).normalized;
            damageable.TakeDamage(_currentData.DamageData, dir);
        }
    }
}

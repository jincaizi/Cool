using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    [RequireComponent(typeof(Collider))]
    public class HitZone : MonoBehaviour
    {
        private IDamageable _owner;
        private readonly HashSet<int> _hitInstanceIds = new();

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        public void Init(IDamageable owner)
        {
            _owner = owner;
        }

        public void ResetHits()
        {
            _hitInstanceIds.Clear();
        }

        private void OnTriggerStay(Collider other)
        {
            var hitbox = other.GetComponent<IAttackHitbox>();
            if (hitbox == null || !hitbox.IsActive) return;
            if (!_hitInstanceIds.Add(hitbox.GetInstanceID())) return;

            Vector3 hitDir = (transform.position - hitbox.GetBounds().center).normalized;

            var data = hitbox.CurrentData;
            if (data != null && data.DamageData != null)
            {
                data.DamageData.KnockbackForce = data.KnockbackForce;
                _owner?.TakeDamage(data.DamageData, hitDir);
            }
        }
    }
}

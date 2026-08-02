using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.VFX;

namespace Hotfix.GameSystems.Combat
{
    [RequireComponent(typeof(Collider))]
    public class HitZone : MonoBehaviour
    {
        private IDamageable _owner;
        private Collider _collider;
        private readonly HashSet<int> _hitInstanceIds = new();

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
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
            if (!other.TryGetComponent<IAttackHitbox>(out var hitbox)) return;
            if (!hitbox.IsActive) return;
            if (!_hitInstanceIds.Add(hitbox.GetInstanceID())) return;

            Vector3 hitDir = (transform.position - hitbox.GetBounds().center).normalized;
            Vector3 hitPoint = _collider.ClosestPoint(hitbox.GetBounds().center);  // 仅首次命中 1 次
            var contact = new HitContact
            {
                HitPoint = hitPoint,
                Part = HitZoneClassifier.Classify(hitPoint, _collider.bounds),
                ElementColor = WeaponVFXController.CurrentElementColor,
                HasContact = true
            };

            var data = hitbox.CurrentData;
            if (data != null && data.DamageData != null)
            {
                data.DamageData.KnockbackForce = data.KnockbackForce;  // 保留既有功能行
                _owner?.TakeDamage(data.DamageData, hitDir, contact);
            }
        }
    }
}

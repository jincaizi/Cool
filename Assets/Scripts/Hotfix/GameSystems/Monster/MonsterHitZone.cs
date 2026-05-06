using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Monster
{
    [RequireComponent(typeof(Collider))]
    public class MonsterHitZone : MonoBehaviour
    {
        private MonsterEntity _owner;
        private readonly HashSet<IAttackHitbox> _hitSources = new();

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        public void Init(MonsterEntity owner)
        {
            _owner = owner;
        }

        private void OnTriggerStay(Collider other)
        {
            var hitbox = other.GetComponent<IAttackHitbox>();
            if (hitbox == null || !hitbox.IsActive || _hitSources.Contains(hitbox)) return;

            _hitSources.Add(hitbox);
            Vector3 dir = (transform.position - hitbox.transform.position).normalized;
            _owner?.TakeDamageFromHitbox(hitbox.CurrentData, dir);
        }
    }
}

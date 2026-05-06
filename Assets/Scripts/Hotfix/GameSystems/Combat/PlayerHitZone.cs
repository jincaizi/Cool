using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    public class PlayerHitZone : MonoBehaviour
    {
        private Sys3C.FSM.FSMManager _fsmManager;
        private readonly HashSet<IAttackHitbox> _hitSources = new();

        public void Init(Sys3C.FSM.FSMManager fsmManager)
        {
            _fsmManager = fsmManager;
        }

        private void OnTriggerStay(Collider other)
        {
            var hitbox = other.GetComponent<IAttackHitbox>();
            if (hitbox == null || !hitbox.IsActive || _hitSources.Contains(hitbox)) return;

            _hitSources.Add(hitbox);

            if (_fsmManager == null) return;

            var damageData = hitbox.CurrentData;
            float damage = damageData != null
                ? damageData.CalculateFinalDamage(null)
                : 10f;

            Vector3 hitDir = (transform.position - hitbox.transform.position).normalized;
            _fsmManager.HandleDamage(
                sourceId: -1,
                damage: damage,
                hitDirection: hitDir,
                knockbackForce: 1f,
                launchForce: 0,
                stunDuration: 0,
                isCritical: false
            );
        }
    }
}

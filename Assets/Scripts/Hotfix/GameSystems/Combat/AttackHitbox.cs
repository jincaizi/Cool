using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    public class AttackHitbox : MonoBehaviour, IAttackHitbox
    {
        public bool IsActive { get; private set; }
        public AttackHitboxData CurrentData { get; private set; }

        private Collider _collider;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            gameObject.SetActive(false);
        }

        public void Activate(DamageBlock damageData, EffectBlock effectData)
        {
            if (damageData != null && effectData != null)
            {
                damageData.KnockbackForce = effectData.KnockbackForce;
            }
            CurrentData = new AttackHitboxData
            {
                DamageData = damageData,
                KnockbackForce = effectData?.KnockbackForce ?? 0,
                LaunchForce = effectData?.LaunchForce ?? 0,
                StunDuration = effectData?.StunDuration ?? 0,
            };
            IsActive = true;
            gameObject.SetActive(true);
        }

        public void Activate(DamageBlock damageData)
        {
            Activate(damageData, null);
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }

        public void TriggerHit() { }

        public Bounds GetBounds()
        {
            if (_collider != null)
                return _collider.bounds;
            return new Bounds(transform.position, Vector3.zero);
        }
    }
}

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

        public void Activate(AttackEffectConfig effectConfig)
        {
            var dmg = effectConfig?.Damage ?? DamageBlock.CreateDefault(10f);
            CurrentData = new AttackHitboxData
            {
                DamageData = dmg,
                KnockbackForce = effectConfig?.KnockbackForce ?? 0,
                LaunchForce = effectConfig?.LaunchForce ?? 0,
                StunDuration = effectConfig?.StunDuration ?? 0,
            };
            IsActive = true;
            gameObject.SetActive(true);
        }

        public void Activate(DamageBlock damageData)
        {
            Activate(new AttackEffectConfig { Damage = damageData });
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

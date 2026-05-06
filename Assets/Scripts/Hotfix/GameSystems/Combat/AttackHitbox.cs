using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
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

        public void Activate(DamageData damageData)
        {
            CurrentData = new AttackHitboxData { DamageData = damageData };
            IsActive = true;
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }

        public void TriggerHit()
        {
            // 命中触发逻辑，根据需求实现
        }

        public Bounds GetBounds()
        {
            if (_collider != null)
                return _collider.bounds;
            return new Bounds(transform.position, Vector3.zero);
        }
    }
}

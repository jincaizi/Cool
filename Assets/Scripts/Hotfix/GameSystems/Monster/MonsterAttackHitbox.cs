using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterAttackHitbox : MonoBehaviour, IAttackHitbox
    {
        public bool IsActive { get; private set; }
        public AttackHitboxData CurrentData { get; private set; }

        private void Awake()
        {
            gameObject.SetActive(false);
        }

        public void Activate(AttackHitboxData data)
        {
            IsActive = true;
            CurrentData = data;
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }

        public void TriggerHit()
        {
            // Monster attack hitbox hit trigger logic (if needed)
        }

        public Bounds GetBounds()
        {
            var col = GetComponent<Collider>();
            return col != null ? col.bounds : new Bounds(transform.position, Vector3.zero);
        }
    }
}
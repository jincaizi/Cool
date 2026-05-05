using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    public interface IMonsterDamageHandler
    {
        GameObject TargetGameObject { get; }
        Transform TargetTransform { get; }
        void OnMonsterAttackHit(DamageData damageData, Vector3 hitDirection);
    }

    public class MonsterAttackHitbox : MonoBehaviour
    {
        public bool IsActive { get; private set; }

        private DamageData _damageData;
        private readonly HashSet<GameObject> _hitTargets = new();

        private void Awake()
        {
            gameObject.SetActive(false);
        }

        public void Activate(DamageData damageData)
        {
            IsActive = true;
            _damageData = damageData;
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

            var handler = other.GetComponentInParent<IMonsterDamageHandler>();
            if (handler == null || _hitTargets.Contains(handler.TargetGameObject)) return;

            _hitTargets.Add(handler.TargetGameObject);

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            Vector3 hitDir = (handler.TargetTransform.position - hitPoint).normalized;
            handler.OnMonsterAttackHit(_damageData, hitDir);
        }
    }
}

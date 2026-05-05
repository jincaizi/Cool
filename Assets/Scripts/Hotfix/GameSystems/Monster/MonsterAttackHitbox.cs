using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterAttackHitbox : MonoBehaviour
    {
        public bool IsActive { get; private set; }
        public DamageData CurrentDamageData { get; private set; }

        private void Awake()
        {
            gameObject.SetActive(false);
        }

        public void Activate(DamageData damageData)
        {
            IsActive = true;
            CurrentDamageData = damageData;
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }
    }
}

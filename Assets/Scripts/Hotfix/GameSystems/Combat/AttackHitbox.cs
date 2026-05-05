using UnityEngine;

namespace Hotfix.GameSystems.Combat
{
    public class AttackHitbox : MonoBehaviour
    {
        public bool IsActive { get; private set; }
        public AttackHitboxData CurrentData { get; private set; }

        private void Awake()
        {
            gameObject.SetActive(false);
        }

        public void Activate(AttackHitboxData data)
        {
            CurrentData = data;
            IsActive = true;
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }
    }
}

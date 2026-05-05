using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    [RequireComponent(typeof(Collider))]
    public class MonsterHitZone : MonoBehaviour
    {
        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }
    }
}

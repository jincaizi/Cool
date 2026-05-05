using UnityEngine;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    public class PlayerHitZone : MonoBehaviour, IMonsterDamageHandler
    {
        private Sys3C.FSM.FSMManager _fsmManager;

        GameObject IMonsterDamageHandler.TargetGameObject => gameObject;
        Transform IMonsterDamageHandler.TargetTransform => transform;

        public void Init(Sys3C.FSM.FSMManager fsmManager)
        {
            _fsmManager = fsmManager;
        }

        void IMonsterDamageHandler.OnMonsterAttackHit(DamageData damageData, Vector3 hitDirection)
        {
            if (_fsmManager == null) return;

            float damage = damageData != null
                ? damageData.CalculateFinalDamage(null)
                : 10f;

            _fsmManager.HandleDamage(
                sourceId: -1,
                damage: damage,
                hitDirection: hitDirection,
                knockbackForce: 1f,
                launchForce: 0,
                stunDuration: 0,
                isCritical: false
            );
        }
    }
}

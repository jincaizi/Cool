using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C
{
    public class CharacterAttackHandler : MonoBehaviour
    {
        [SerializeField] private LayerMask _targetMask = -1;
        private IWeapon _currentWeapon;

        private void Start()
        {
            _currentWeapon = GetComponent<IWeapon>();
            if (_currentWeapon == null)
                _currentWeapon = GetComponentInChildren<IWeapon>();
        }

        public void EquipWeapon(IWeapon weapon) => _currentWeapon = weapon;

        public void OnAttackActivated()
        {
            if (_currentWeapon == null || !_currentWeapon.CanAttack()) return;
            _currentWeapon.Attack(transform.forward, _targetMask);
        }
    }
}

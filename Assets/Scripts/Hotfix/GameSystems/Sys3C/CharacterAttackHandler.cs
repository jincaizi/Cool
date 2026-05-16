using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C
{
    public class CharacterAttackHandler : MonoBehaviour
    {
        [SerializeField] private LayerMask _targetMask = -1;
        [SerializeField] private SelectionRing _selectionRing;

        private IWeapon _currentWeapon;
        private ITargetable _currentTarget;
        private Action _onTargetDeath;

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
            var hits = _currentWeapon.Attack(transform.forward, _targetMask);
            if (hits.Count > 0)
                SelectTarget(hits[0]);
        }

        public void SelectTarget(IDamageable target)
        {
            if (!(target is ITargetable targetable) || targetable == _currentTarget) return;
            if (_selectionRing == null) return;

            // Unsubscribe old target's death event
            if (_currentTarget != null && _onTargetDeath != null)
            {
                _currentTarget.OnDeath -= _onTargetDeath;
                _onTargetDeath = null;
            }

            _selectionRing.Detach();

            float yOffset = -0.9f;

            _currentTarget = targetable;
            _onTargetDeath = () =>
            {
                if (_currentTarget != null)
                    _currentTarget.OnDeath -= _onTargetDeath;
                _selectionRing.Detach();
                _currentTarget = null;
                _onTargetDeath = null;
            };
            _currentTarget.OnDeath += _onTargetDeath;

            _selectionRing.AttachTo(target.Transform, yOffset);
        }
    }
}

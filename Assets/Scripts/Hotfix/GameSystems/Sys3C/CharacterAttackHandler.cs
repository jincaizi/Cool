using System;
using Cysharp.Threading.Tasks;
using Hotfix.GameSystems.UI;
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

            if (_selectionRing == null)
                _selectionRing = GetComponentInChildren<SelectionRing>();
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

            DetachCurrentTarget();

            _currentTarget = targetable;

            // Selection ring
            if (_selectionRing != null)
            {
                _selectionRing.AttachTo(target.Transform, targetable.SelectionRingYOffset);
            }

            // Target panel
            var targetPanel = UIManager.Instance?.GetPanel<TargetPanel>("TargetPanel");
            if (targetPanel != null)
            {
                targetPanel.Bind(_currentTarget);
                UIManager.Instance.ShowAlwaysAsync("TargetPanel").Forget();
            }

            _onTargetDeath = () =>
            {
                if (_currentTarget != null)
                    _currentTarget.OnDeath -= _onTargetDeath;
                DetachCurrentTarget();
                _onTargetDeath = null;
            };
            _currentTarget.OnDeath += _onTargetDeath;
        }

        private void DetachCurrentTarget()
        {
            if (_currentTarget == null) return;

            _currentTarget.OnDeath -= _onTargetDeath;

            if (_selectionRing != null)
                _selectionRing.Detach();

            var targetPanel = UIManager.Instance?.GetPanel<TargetPanel>("TargetPanel");
            if (targetPanel != null)
            {
                targetPanel.Clear();
                UIManager.Instance.HideAlwaysAsync("TargetPanel").Forget();
            }

            _currentTarget = null;
        }
    }
}

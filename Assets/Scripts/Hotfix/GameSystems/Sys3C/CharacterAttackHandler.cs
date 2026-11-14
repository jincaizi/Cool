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
        [SerializeField] private Animator _animator;

        private IWeapon _currentWeapon;
        private ITargetable _currentTarget;
        private Action _onTargetDeath;
        private Func<int, int, IWeapon>? _weaponFactory;

        /// <summary>当前选中目标（目标面板/选中环显示的那个）；无目标为 null。</summary>
        public ITargetable CurrentTarget => _currentTarget;

        private void Start()
        {
            _currentWeapon = GetComponent<IWeapon>();
            if (_currentWeapon == null)
                _currentWeapon = GetComponentInChildren<IWeapon>();

            if (_selectionRing == null)
                _selectionRing = GetComponentInChildren<SelectionRing>();
        }

        public void EquipWeapon(IWeapon weapon) => _currentWeapon = weapon;

        /// <summary>由 Sys3CEntry 注入（避免在 MonoBehaviour 上 GetComponent）。</summary>
        public void SetAnimator(Animator animator) => _animator = animator;

        /// <summary>按模板/元素创建武器表现；工厂为空时只保留权威状态，不替换模型。</summary>
        public void SetWeaponFactory(Func<int, int, IWeapon>? factory) => _weaponFactory = factory;

        public void EquipWeapon(int templateId, int elementId)
        {
            var weapon = _weaponFactory?.Invoke(templateId, elementId);
            if (weapon != null) EquipWeapon(weapon);
        }

        /// <summary>攻击 Animator 的 AttackSpeed 参数（0.25–4，禁止全局 Animator.speed）。</summary>
        public void SetAttackSpeed(float multiplier)
        {
            if (_animator != null)
                _animator.SetFloat(Hotfix.GameSystems.Sys3C.Animation.AnimHashes.AttackSpeed,
                    Mathf.Clamp(multiplier, 0.25f, 4f));
        }

        public void OnAttackActivated()
        {
            if (_currentWeapon == null || !_currentWeapon.CanAttack()) return;
            var hits = _currentWeapon.Attack(transform.forward, _targetMask);
            if (hits.Count > 0)
                SelectTarget(hits[0]);
        }

        public void SelectTarget(IDamageable target)
        {
            // 死亡目标（含尸体）不可选中——否则光环会 re-parent 到尸体上并被随尸体销毁
            if (target is IDamageable d && !d.IsAlive)
                return;

            if (!(target is ITargetable targetable) || targetable == _currentTarget) return;

            DetachCurrentTarget();

            _currentTarget = targetable;

            // Selection ring
            if (_selectionRing != null)
            {
                _selectionRing.AttachTo(target.Transform, targetable.SelectionRingYOffset);
            }

            // Target panel
            var targetPanel = UIManager.Instance?.GetPanel<TargetPanel>();
            if (targetPanel != null)
            {
                targetPanel.Bind(_currentTarget);
                UIManager.Instance.ShowAlwaysAsync<TargetPanel>().Forget();
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

            var targetPanel = UIManager.Instance?.GetPanel<TargetPanel>();
            if (targetPanel != null)
            {
                targetPanel.Clear();
                UIManager.Instance.HideAlwaysAsync<TargetPanel>().Forget();
            }

            _currentTarget = null;
        }
    }
}

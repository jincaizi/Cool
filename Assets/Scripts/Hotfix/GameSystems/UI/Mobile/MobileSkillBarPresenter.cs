using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Sys3C.Input;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public class MobileSkillBarPresenter : MonoBehaviour
    {
        [SerializeField] private MobileInputBinder _inputBinder;
        [SerializeField] private MobileSkillButton[] _skillButtons;
        [SerializeField] private AttackHeavyReadyView _attackReadyView;
        [SerializeField] private MobileSkillAimJoystick _fireballAim;

        private void Update()
        {
            var entry = _inputBinder != null ? _inputBinder.BoundEntry : null;
            var coordinator = entry != null ? entry.SkillCoordinator : null;
            if (coordinator == null)
            {
                ResetViews();
                return;
            }

            if (_skillButtons != null)
            {
                for (int i = 0; i < _skillButtons.Length; i++)
                {
                    var button = _skillButtons[i];
                    if (button == null) continue;
                    button.RenderCooldown(coordinator.GetCooldownInfo(button.SkillId));
                    float charge = 0f;
                    bool holdToAim = false;
                    if (coordinator.CurrentSkill != null
                        && coordinator.CurrentSkill.SkillId == button.SkillId
                        && coordinator.CurrentSubState == SkillSubState.Charging)
                    {
                        holdToAim = coordinator.CurrentSkill.Data is ProjectileSkillData p && p.HoldToAim;
                        charge = holdToAim ? 0f : coordinator.CurrentSkill.GetChargeProgress();
                    }
                    button.RenderCharge(charge);
                }
            }

            if (_fireballAim != null)
            {
                bool fireballCharging = coordinator.CurrentSkill != null
                    && coordinator.CurrentSkill.SkillId == (int)SkillID.Fireball
                    && coordinator.CurrentSubState == SkillSubState.Charging;

                if (_fireballAim.IsGestureActive && !fireballCharging)
                    _fireballAim.ResetGesture();

                bool canStart = entry.Stamina != null && entry.Stamina.CanStartConsuming
                    && !coordinator.GetCooldownInfo((int)SkillID.Fireball).IsOnCooldown;
                _fireballAim.Interactable = fireballCharging || canStart;
            }

            if (_attackReadyView != null) _attackReadyView.SetReady(entry.CanFireHeavy);
        }

        private void OnDisable() => ResetViews();

        private void ResetViews()
        {
            if (_skillButtons != null)
            {
                for (int i = 0; i < _skillButtons.Length; i++)
                {
                    if (_skillButtons[i] == null) continue;
                    _skillButtons[i].RenderCooldown(default);
                    _skillButtons[i].RenderCharge(0f);
                }
            }
            if (_attackReadyView != null) _attackReadyView.SetReady(false);
            _fireballAim?.ResetGesture();
        }
    }
}

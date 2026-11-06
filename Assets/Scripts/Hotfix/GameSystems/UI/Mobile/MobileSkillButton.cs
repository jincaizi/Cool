using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class MobileSkillButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private SkillID _skillId;
        [SerializeField] private MobileActionButtons _buttons;
        [SerializeField] private Image _cooldownFill;
        [SerializeField] private Image _chargeFill;
        [SerializeField] private TMP_Text _countdownText;

        public int SkillId => (int)_skillId;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_buttons == null) return;
            switch (_skillId)
            {
                case SkillID.SkillQ: _buttons.OnSkillQDown(); break;
                case SkillID.SkillR: _buttons.OnSkillRDown(); break;
                case SkillID.Fireball: _buttons.OnFireballDown(); break;
                case SkillID.ChargeBlast: _buttons.OnChargeBlastDown(); break;
                case SkillID.Ultimate: _buttons.OnMeteorDown(); break;
            }
        }

        public void OnPointerUp(PointerEventData eventData) => ReleaseHeldSkill();

        public void RenderCooldown(CooldownInfo info)
        {
            bool visible = info.IsOnCooldown && info.Remaining > 0f && info.Total > 0f;
            if (_cooldownFill != null)
            {
                _cooldownFill.fillAmount = visible ? Mathf.Clamp01(info.Remaining / info.Total) : 0f;
                _cooldownFill.gameObject.SetActive(visible);
            }
            if (_countdownText != null)
            {
                _countdownText.text = visible ? Mathf.CeilToInt(info.Remaining).ToString() : string.Empty;
                _countdownText.gameObject.SetActive(visible);
            }
        }

        public void RenderCharge(float normalized)
        {
            bool visible = normalized > 0f;
            if (_chargeFill == null) return;
            _chargeFill.fillAmount = visible ? Mathf.Clamp01(normalized) : 0f;
            _chargeFill.gameObject.SetActive(visible);
        }

        private void OnDisable()
        {
            ReleaseHeldSkill();
            RenderCharge(0f);
        }

        private void ReleaseHeldSkill()
        {
            if (_buttons == null) return;
            if (_skillId == SkillID.Fireball) _buttons.OnFireballUp();
            if (_skillId == SkillID.ChargeBlast) _buttons.OnChargeBlastUp();
        }
    }
}

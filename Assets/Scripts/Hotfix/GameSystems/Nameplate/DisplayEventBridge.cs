using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class DisplayEventBridge
    {
        private readonly FloatTextRenderer _floatText;
        private readonly DamageScreenEffect _damageScreenEffect;
        private readonly FloatTextSettings _damageSettings;
        private readonly FloatTextSettings _critDamageSettings;
        private readonly FloatTextSettings _skillNameSettings;

        public DisplayEventBridge(
            FloatTextRenderer floatText,
            DamageScreenEffect damageScreenEffect,
            FloatTextSettings damageSettings,
            FloatTextSettings critDamageSettings,
            FloatTextSettings skillNameSettings)
        {
            _floatText = floatText;
            _damageScreenEffect = damageScreenEffect;
            _damageSettings = damageSettings;
            _critDamageSettings = critDamageSettings;
            _skillNameSettings = skillNameSettings;
        }

        public void Enable()
        {
            EventBus.Subscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Subscribe<SkillActivatedEvent>(OnSkillActivated);
        }

        public void Disable()
        {
            EventBus.Unsubscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Unsubscribe<SkillActivatedEvent>(OnSkillActivated);
        }

        private void OnPlayerDamaged(DamageEvent e)
        {
            _damageScreenEffect.Flash();
            var settings = e.IsCritical ? _critDamageSettings : _damageSettings;
            _floatText.ShowDamageText(e.TargetId, Vector3.up * 2f, settings, Mathf.CeilToInt(e.Damage));
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var settings = e.IsCritical ? _critDamageSettings : _damageSettings;
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, settings, e.Damage);
        }

        private void OnSkillActivated(SkillActivatedEvent e)
        {
            _floatText.ShowFloatingText(Vector3.zero, _skillNameSettings, e.SkillName);
        }
    }
}

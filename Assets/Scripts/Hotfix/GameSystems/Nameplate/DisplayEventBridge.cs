using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Nameplate
{
    public class DisplayEventBridge
    {
        private readonly FloatTextRenderer _floatText;
        private readonly FloatTextSettings _damageSettings;
        private readonly FloatTextSettings _critDamageSettings;

        public DisplayEventBridge(
            FloatTextRenderer floatText,
            FloatTextSettings damageSettings,
            FloatTextSettings critDamageSettings)
        {
            _floatText = floatText;
            _damageSettings = damageSettings;
            _critDamageSettings = critDamageSettings;
        }

        public void Enable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
        }

        public void Disable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var settings = e.IsCritical ? _critDamageSettings : _damageSettings;
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, settings, e.Damage);
        }
    }
}

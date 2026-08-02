using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Nameplate
{
    public class DisplayEventBridge
    {
        private readonly FloatTextRenderer _floatText;
        private readonly FloatTextSettings _damageSettings;
        private readonly FloatTextSettings _critDamageSettings;
        private readonly FloatTextSettings _playerDamageSettings;

        public DisplayEventBridge(
            FloatTextRenderer floatText,
            FloatTextSettings damageSettings,
            FloatTextSettings critDamageSettings,
            FloatTextSettings playerDamageSettings)
        {
            _floatText = floatText;
            _damageSettings = damageSettings;
            _critDamageSettings = critDamageSettings;
            _playerDamageSettings = playerDamageSettings;
        }

        public void Enable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Subscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
        }

        public void Disable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Unsubscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
        }

        private void OnPlayerDamaged(PlayerTakeDamageEvent e)
        {
            // 玩家掉血飘字使用专属配置(灰字+黑描边),与怪物掉血(红字)区分
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, _playerDamageSettings, e.Damage);
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var settings = e.IsCritical ? _critDamageSettings : _damageSettings;
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, settings, e.Damage);
        }
    }
}

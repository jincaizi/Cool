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
            EventBus.Subscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
        }

        public void Disable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Unsubscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
        }

        private void OnPlayerDamaged(PlayerTakeDamageEvent e)
        {
            // 玩家飘字复用怪物同款 settings（后续需要差异化时再新增配置）
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, _damageSettings, e.Damage);
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var settings = e.IsCritical ? _critDamageSettings : _damageSettings;
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, settings, e.Damage);
        }
    }
}

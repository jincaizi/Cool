using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Nameplate
{
    public class DisplayEventBridge
    {
        private const float BlockMergeWindow = 0.2f;

        private readonly FloatTextRenderer _floatText;
        private readonly FloatTextSettings _damageSettings;
        private readonly FloatTextSettings _critDamageSettings;
        private readonly FloatTextSettings _playerDamageSettings;
        private readonly FloatTextSettings _blockSettings;
        private readonly Dictionary<int, float> _lastBlockTime = new Dictionary<int, float>();
        private bool _warnedMissingBlockSettings;

        public DisplayEventBridge(
            FloatTextRenderer floatText,
            FloatTextSettings damageSettings,
            FloatTextSettings critDamageSettings,
            FloatTextSettings playerDamageSettings,
            FloatTextSettings blockSettings)
        {
            _floatText = floatText;
            _damageSettings = damageSettings;
            _critDamageSettings = critDamageSettings;
            _playerDamageSettings = playerDamageSettings;
            _blockSettings = blockSettings;
        }

        public void Enable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Subscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
            EventBus.Subscribe<MonsterDotDamageEvent>(OnMonsterDotDamaged);
            EventBus.Subscribe<BlockSucceededEvent>(OnBlockSucceeded);
        }

        public void Disable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Unsubscribe<PlayerTakeDamageEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<MonsterDotDamageEvent>(OnMonsterDotDamaged);
            EventBus.Unsubscribe<BlockSucceededEvent>(OnBlockSucceeded);
            _lastBlockTime.Clear();
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

        private void OnMonsterDotDamaged(MonsterDotDamageEvent e)
        {
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, _damageSettings, e.Damage);
        }

        private void OnBlockSucceeded(BlockSucceededEvent e)
        {
            if (!FloatTextRenderer.EnableVFX) return;
            if (_blockSettings == null)
            {
                if (!_warnedMissingBlockSettings)
                {
                    _warnedMissingBlockSettings = true;
                    Debug.LogWarning("[DisplayEventBridge] _blockSettings 未接线——格挡飘字不会显示");
                }
                return;
            }

            float now = Time.time;
            if (_lastBlockTime.TryGetValue(e.EntityId, out float last)
                && now - last < BlockMergeWindow) return;
            _lastBlockTime[e.EntityId] = now;

            _floatText.ShowFloatingText(e.TextPosition, _blockSettings, "格挡");
        }
    }
}

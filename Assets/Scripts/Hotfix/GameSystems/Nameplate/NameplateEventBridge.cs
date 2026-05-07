using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class NameplateEventBridge : MonoBehaviour
    {
        private void OnEnable()
        {
            EventBus.Subscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Subscribe<SkillActivatedEvent>(OnSkillActivated);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Unsubscribe<SkillActivatedEvent>(OnSkillActivated);
        }

        private void OnPlayerDamaged(DamageEvent e)
        {
            var cfg = e.IsCritical ? FloatingTextPresets.CritDamage : FloatingTextPresets.Damage;
            var posEstimate = Vector3.up * 2f;
            FloatingTextPool.Instance?.Spawn(posEstimate, $"-{Mathf.CeilToInt(e.Damage)}", cfg);
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var cfg = e.IsCritical ? FloatingTextPresets.CritDamage : FloatingTextPresets.Damage;
            FloatingTextPool.Instance?.Spawn(e.HitPosition, $"-{e.Damage}", cfg);
        }

        private void OnSkillActivated(SkillActivatedEvent e)
        {
            FloatingTextPool.Instance?.Spawn(
                e.CasterPosition + Vector3.up * 2.5f,
                e.SkillName,
                FloatingTextPresets.SkillName
            );
        }
    }
}

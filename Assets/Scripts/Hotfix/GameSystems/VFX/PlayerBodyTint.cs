using DataDefinition;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>本地玩家低血量持续泛红 + 受击泛红（期间不可重触发）。</summary>
    public sealed class PlayerBodyTint : MonoBehaviour
    {
        private IPlayerHpSource _hp;
        private BodyTintRenderer _tint;
        private readonly HitTintState _state = new HitTintState();
        private GameSettings _settings;

        private void Awake()
        {
            _hp = GetComponent<Sys3CEntry>();
            _settings = GameSettings.Instance;
            _tint = new BodyTintRenderer(GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        /// <summary>测试/宿主可注入替代的 HP 来源。</summary>
        public void Bind(IPlayerHpSource hp) => _hp = hp;

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerTakeDamageEvent>(OnDamaged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerTakeDamageEvent>(OnDamaged);
            _tint?.Clear();
        }

        private void OnDestroy()
        {
            _tint?.Clear();
        }

        private void OnDamaged(PlayerTakeDamageEvent e)
        {
            if (_settings == null || e.EntityId != gameObject.GetInstanceID()) return;
            _state.TryTrigger(_settings.HitTintDuration);
        }

        private void Update()
        {
            if (_settings == null || _tint == null) return;

            _state.Tick(Time.deltaTime);

            float hpPercent = _hp != null ? _hp.HpPercent : 1f;
            bool lowActive = _hp != null && !_hp.IsDead && hpPercent > 0f
                             && hpPercent < _settings.LowHpThreshold;

            float lowAmount = _settings.LowHpTintColor.a * (lowActive ? 1f : 0f);
            float hitAmount = _settings.HitTintColor.a * _state.Amount(_settings.HitTintDuration);

            _tint.Apply(_settings.LowHpTintColor, lowAmount,
                _settings.HitTintColor, hitAmount);
        }
    }
}

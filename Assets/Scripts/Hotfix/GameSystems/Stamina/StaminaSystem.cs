using UnityEngine;

namespace Hotfix.GameSystems.Stamina
{
    public struct StaminaConfig
    {
        public float Max;
        public float SprintDrainPerSecond;
        public float DefendDrainPerSecond;
        public float RegenPerSecond;
        public float RegenDelay;
        public float ExhaustRecoverRatio;

        public static StaminaConfig Default => new StaminaConfig
        {
            Max = 100f,
            SprintDrainPerSecond = 20f,
            DefendDrainPerSecond = 10f,
            RegenPerSecond = 100f / 3f,
            RegenDelay = 0.75f,
            ExhaustRecoverRatio = 0.2f
        };
    }

    /// <summary>
    /// 精力资源模型（纯 C#，无 UI 依赖）。调用方每帧先 TryConsume，最后 Tick 一次。
    /// </summary>
    public sealed class StaminaSystem
    {
        private const float FullEpsilon = 0.001f;
        private const float MaxDeltaTime = 0.1f;

        private readonly float _max;
        private readonly float _regenPerSecond;
        private readonly float _regenDelay;
        private readonly float _exhaustRecoverRatio;

        private float _current;
        private float _sinceLastConsume;
        private bool _exhausted;

        public StaminaSystem(StaminaConfig config)
        {
            var defaults = StaminaConfig.Default;
            _max = FiniteOrDefault(config.Max, defaults.Max, 1f);
            _regenPerSecond = FiniteOrDefault(config.RegenPerSecond, defaults.RegenPerSecond, 0f);
            _regenDelay = FiniteOrDefault(config.RegenDelay, defaults.RegenDelay, 0f);
            _exhaustRecoverRatio = Mathf.Clamp01(
                FiniteOrDefault(config.ExhaustRecoverRatio, defaults.ExhaustRecoverRatio, 0f));

            _current = _max;
            _sinceLastConsume = float.PositiveInfinity;
        }

        public float Max => _max;
        public float Current => _current;
        public float Ratio => _max > 0f ? Mathf.Clamp01(_current / _max) : 0f;
        public bool IsExhausted => _exhausted;
        public bool IsFull => _current >= _max - FullEpsilon;
        public bool CanStartConsuming => !_exhausted && _current > 0f;

        /// <summary>
        /// 扣精。非有限或 &lt;=0 的 amount 是 no-op（不重置回复延迟）。
        /// 恰好扣到 0 也视为力竭并返回 false（调用方必须立即停止动作）。
        /// </summary>
        public bool TryConsume(float amount)
        {
            if (!IsFinite(amount) || amount <= 0f) return true;

            _sinceLastConsume = 0f;
            if (_current <= amount)
            {
                _current = 0f;
                _exhausted = true;
                return false;
            }

            _current -= amount;
            return true;
        }

        /// <summary>每帧调用一次；消耗/失败尝试之后调用。</summary>
        public void Tick(float deltaTime)
        {
            float dt = IsFinite(deltaTime) && deltaTime > 0f
                ? Mathf.Min(deltaTime, MaxDeltaTime)
                : 0f;
            if (dt <= 0f) return;

            _sinceLastConsume += dt;
            if (_sinceLastConsume < _regenDelay) return;

            if (_current < _max)
                _current = Mathf.Min(_max, _current + _regenPerSecond * dt);

            if (_exhausted && _current >= _max * _exhaustRecoverRatio)
                _exhausted = false;
        }

        /// <summary>复活时回满并清除力竭。</summary>
        public void Refill()
        {
            _current = _max;
            _exhausted = false;
            _sinceLastConsume = 0f;
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float FiniteOrDefault(float value, float fallback, float min)
            => IsFinite(value) ? Mathf.Max(min, value) : fallback;
    }
}

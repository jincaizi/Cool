using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>受击泛红的触发门与计时：泛红期间不允许再次触发。</summary>
    public sealed class HitTintState
    {
        private float _remaining;

        public bool IsHitActive => _remaining > 0f;

        public bool TryTrigger(float duration)
        {
            if (_remaining > 0f || duration <= 0f) return false;
            _remaining = duration;
            return true;
        }

        public void Tick(float deltaTime)
        {
            _remaining = Mathf.Max(0f, _remaining - deltaTime);
        }

        public float Amount(float duration)
        {
            return duration > 0f ? Mathf.Clamp01(_remaining / duration) : 0f;
        }
    }
}

using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 摇杆满推冲刺手势的状态机（纯 C#，无 MonoBehaviour）。
    /// 模长 ≥ enterThreshold 并保持 holdSeconds 秒进入；回落到 ≤ exitThreshold 退出。
    /// 迟滞避免手指停在阈值附近时反复开关；保持时间过滤快速甩杆/微调。
    /// </summary>
    public sealed class SprintGestureState
    {
        private readonly float _enterThreshold;
        private readonly float _exitThreshold;
        private readonly float _holdSeconds;

        private bool _held;
        private float _timer;

        public SprintGestureState(float enterThreshold = 0.9f, float exitThreshold = 0.8f,
            float holdSeconds = 0.2f)
        {
            _enterThreshold = IsFinite(enterThreshold)
                ? Mathf.Clamp(enterThreshold, 0.01f, 1f)
                : 0.9f;

            float clampedExit = IsFinite(exitThreshold)
                ? Mathf.Clamp(exitThreshold, 0f, _enterThreshold)
                : 0.8f;
            _exitThreshold = Mathf.Clamp(Mathf.Min(clampedExit, _enterThreshold - 0.01f), 0f, 1f);

            _holdSeconds = IsFinite(holdSeconds) ? Mathf.Max(0f, holdSeconds) : 0.2f;
        }

        public bool IsSprinting => _held;

        /// <summary>每帧调用；返回是否处于冲刺。</summary>
        public bool Tick(float magnitude, float deltaTime)
        {
            if (!IsFinite(magnitude)) magnitude = 0f;
            if (!IsFinite(deltaTime) || deltaTime <= 0f) deltaTime = 0f;

            if (_held)
            {
                if (magnitude <= _exitThreshold)
                {
                    _held = false;
                    _timer = 0f;
                }
                return _held;
            }

            if (magnitude >= _enterThreshold)
            {
                _timer += deltaTime;
                if (_timer >= _holdSeconds)
                {
                    _held = true;
                    _timer = 0f;
                }
            }
            else
            {
                _timer = 0f;
            }

            return _held;
        }

        public void Reset()
        {
            _held = false;
            _timer = 0f;
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

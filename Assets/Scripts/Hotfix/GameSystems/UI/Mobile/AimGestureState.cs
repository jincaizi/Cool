using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public enum AimReleaseKind
    {
        None = 0,
        Fire = 1,
        Cancel = 2
    }

    /// <summary>瞄准摇杆纯逻辑：按下点为原点，半径归一，取消带回滞。</summary>
    public sealed class AimGestureState
    {
        private readonly float _radius;
        private readonly float _deadZone;
        private readonly float _cancelEnterScale;
        private readonly float _cancelExitScale;

        private Vector2 _pressLocal;
        private Vector2 _delta;
        private bool _active;
        private bool _cancelArmed;

        public AimGestureState(float radius, float deadZone, float cancelEnterScale, float cancelExitScale)
        {
            _radius = Mathf.Max(1f, radius);
            _deadZone = Mathf.Clamp01(deadZone);
            _cancelEnterScale = Mathf.Max(1f, cancelEnterScale);
            _cancelExitScale = Mathf.Clamp(cancelExitScale, 1f, _cancelEnterScale);
        }

        public bool IsActive => _active;
        public bool CancelArmed => _cancelArmed;
        public Vector2 IconOffset => _active ? Vector2.ClampMagnitude(_delta, _radius) : Vector2.zero;
        public Vector2 Value
        {
            get
            {
                if (!_active) return Vector2.zero;
                Vector2 clamped = Vector2.ClampMagnitude(_delta, _radius);
                return clamped.magnitude <= _deadZone * _radius
                    ? Vector2.zero
                    : clamped / _radius;
            }
        }

        public void Press(Vector2 localPoint)
        {
            _pressLocal = localPoint;
            _delta = Vector2.zero;
            _active = true;
            _cancelArmed = false;
        }

        public void Drag(Vector2 localPoint)
        {
            if (!_active) return;
            _delta = localPoint - _pressLocal;
            float magnitude = _delta.magnitude;
            if (!_cancelArmed && magnitude > _radius * _cancelEnterScale)
                _cancelArmed = true;
            else if (_cancelArmed && magnitude < _radius * _cancelExitScale)
                _cancelArmed = false;
        }

        public AimReleaseKind Release()
        {
            if (!_active) return AimReleaseKind.None;
            var kind = _cancelArmed ? AimReleaseKind.Cancel : AimReleaseKind.Fire;
            Reset();
            return kind;
        }

        public void Reset()
        {
            _active = false;
            _delta = Vector2.zero;
            _cancelArmed = false;
        }
    }
}

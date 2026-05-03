using System;

namespace Hotfix.GameSystems.Sys3C.FSM
{
    /// <summary>
    /// Hit 层状态
    /// </summary>
    public enum HitState
    {
        None,
        Hit,
        Knockback,
        Down,
        Death
    }

    /// <summary>
    /// 受击状态机 - 管理受击/击退/倒地/死亡
    /// </summary>
    public class HitFSM
    {
        private HitState _currentState;

        public HitState CurrentState => _currentState;
        public bool HasSuperArmor => _currentState == HitState.Death;

        public event Action<HitState> OnStateChanged;

        public HitFSM()
        {
            _currentState = HitState.None;
        }

        public void Update(float deltaTime)
        {
            // 根据状态处理逻辑
        }

        /// <summary>
        /// 进入受击状态
        /// </summary>
        public void EnterHit(float knockbackForce = 0f)
        {
            var target = knockbackForce > 0 ? HitState.Knockback : HitState.Hit;
            TransitionTo(target);
        }

        /// <summary>
        /// 进入死亡状态
        /// </summary>
        public void EnterDeath()
        {
            TransitionTo(HitState.Death);
        }

        /// <summary>
        /// 重置到无受击状态
        /// </summary>
        public void Reset()
        {
            TransitionTo(HitState.None);
        }

        private void TransitionTo(HitState target)
        {
            if (_currentState == target) return;

            var previous = _currentState;
            _currentState = target;
            OnStateChanged?.Invoke(target);
            UnityEngine.Debug.Log($"[HitFSM] {_currentState}");
        }
    }
}
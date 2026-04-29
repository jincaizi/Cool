using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation
{
    /// <summary>
    /// Hit 管理器 — 处理受击叠加层
    /// </summary>
    public class HitManager
    {
        private readonly Animator _animator;
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");
        private static readonly int HASH_IsHit = Animator.StringToHash("IsHit");

        private const int HIT_LAYER_INDEX = 2;

        public HitManager(Animator animator)
        {
            _animator = animator;
        }

        /// <summary>
        /// 触发受击动画
        /// </summary>
        public void TriggerHit()
        {
            _animator.SetTrigger(HASH_Hit);
            _animator.SetBool(HASH_IsHit, true);
            Debug.Log("[HitManager] TriggerHit called");

            // 设置 Hit 层权重（由 StateMachineBehaviour 控制）
            // 这里主要是通知
        }

        /// <summary>
        /// Hit 动画完成回调
        /// </summary>
        public void OnHitCompleted()
        {
            _animator.SetBool(HASH_IsHit, false);
            Debug.Log("[HitManager] OnHitCompleted");

            // Hit 层权重归零，状态机自动返回
        }

        /// <summary>
        /// 获取 Hit 层权重
        /// </summary>
        public float GetHitLayerWeight()
        {
            return _animator.GetLayerWeight(HIT_LAYER_INDEX);
        }

        /// <summary>
        /// 设置 Hit 层权重
        /// </summary>
        public void SetHitLayerWeight(float weight)
        {
            _animator.SetLayerWeight(HIT_LAYER_INDEX, weight);
        }
    }
}

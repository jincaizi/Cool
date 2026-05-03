using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class HitStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");
        private static AnimationDriver _driver;
        private static Action<string> _onAnimationCompleted;

        // 防止重复触发的标记
        private static bool _hasTriggeredHitComplete;
        private static float _lastNormalizedTime;

        public static void SetCallback(AnimationDriver driver, Action<string> callback)
        {
            _driver = driver;
            _onAnimationCompleted = callback;
            _hasTriggeredHitComplete = false;
            _lastNormalizedTime = 0f;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_Hit)
            {
                _hasTriggeredHitComplete = false;
                _lastNormalizedTime = 0f;
                Debug.Log("[HitBehaviour] Hit entered");
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_Hit && !_hasTriggeredHitComplete)
            {
                // 检测 normalizedTime 从低到高跨过 0.9 的那一刻
                // 对于循环动画，normalizedTime 会从 0 变到 1 然后重置
                // 我们只在 normalizedTime >= 0.9 且比上一帧高的时候触发
                if (stateInfo.normalizedTime >= 0.9f && stateInfo.normalizedTime > _lastNormalizedTime)
                {
                    _hasTriggeredHitComplete = true;
                    Debug.Log("[HitBehaviour] Hit completed, normalizedTime: " + stateInfo.normalizedTime);
                    _onAnimationCompleted?.Invoke("Hit");
                }
                _lastNormalizedTime = stateInfo.normalizedTime;
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // Hit 动画结束后清理
            _hasTriggeredHitComplete = false;
            _lastNormalizedTime = 0f;
        }
    }
}
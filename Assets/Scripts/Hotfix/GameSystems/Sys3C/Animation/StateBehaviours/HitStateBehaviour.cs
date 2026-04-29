using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class HitStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");

        private static AnimationDriver _driver;
        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(AnimationDriver driver, Action<string> callback)
        {
            _driver = driver;
            _onAnimationCompleted = callback;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_Hit)
            {
                Debug.Log("[HitBehaviour] Hit entered");
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_Hit)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    Debug.Log("[HitBehaviour] Hit completed");
                    _onAnimationCompleted?.Invoke("Hit");
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // Hit 动画结束后，Layer 权重归零，自然返回
        }
    }
}
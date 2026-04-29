using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class BaseStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_JumpStart = Animator.StringToHash("JumpStart");
        private static readonly int HASH_JumpAir = Animator.StringToHash("JumpAir");
        private static readonly int HASH_JumpEnd = Animator.StringToHash("JumpEnd");

        private static AnimationDriver _driver;
        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(AnimationDriver driver, Action<string> callback)
        {
            _driver = driver;
            _onAnimationCompleted = callback;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                Debug.Log("[BaseBehaviour] JumpEnd entered");
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    Debug.Log("[BaseBehaviour] JumpEnd completed, normalizedTime=" + stateInfo.normalizedTime);
                    _onAnimationCompleted?.Invoke("JumpEnd");
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                Debug.Log("[BaseBehaviour] JumpEnd exited");
            }
        }
    }
}
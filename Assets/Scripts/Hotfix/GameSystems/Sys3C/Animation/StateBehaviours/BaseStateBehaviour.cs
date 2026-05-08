using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class BaseStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_JumpStart = Animator.StringToHash("JumpStart");
        private static readonly int HASH_JumpAir = Animator.StringToHash("JumpAir");
        private static readonly int HASH_JumpEnd = Animator.StringToHash("JumpEnd");

        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(Action<string> callback)
        {
            _onAnimationCompleted = callback;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    _onAnimationCompleted?.Invoke("JumpEnd");
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }
    }
}
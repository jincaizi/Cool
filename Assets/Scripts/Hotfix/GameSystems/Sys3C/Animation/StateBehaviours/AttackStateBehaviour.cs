using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class AttackStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_Attack1 = Animator.StringToHash("Attack1");
        private static readonly int HASH_Attack2 = Animator.StringToHash("Attack2");

        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(Action<string> callback)
        {
            _onAnimationCompleted = callback;
        }

        private bool IsPlayingClip(AnimatorStateInfo stateInfo)
        {
            // Only fire for states that have an actual animation clip.
            // The default/empty entry state has length == 0.
            return stateInfo.length > 0f;
        }

        private string GetStateName(AnimatorStateInfo stateInfo)
        {
            var hash = stateInfo.shortNameHash;
            if (hash == HASH_Attack1) return "Attack1";
            if (hash == HASH_Attack2) return "Attack2";
            return "AttackSkill";
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (IsPlayingClip(stateInfo) && stateInfo.normalizedTime >= 0.95f && stateInfo.normalizedTime < 1.1f)
            {
                _onAnimationCompleted?.Invoke(GetStateName(stateInfo));
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // Fire for actual animation states (length > 0) that exit.
            // The default Empty state (length == 0) is excluded to prevent
            // premature cleanup when SetTrigger starts a new animation.
            // This also catches fast animations where OnStateUpdate's 95%
            // window is skipped in a single frame.
            if (IsPlayingClip(stateInfo))
            {
                _onAnimationCompleted?.Invoke(GetStateName(stateInfo));
            }
        }
    }
}

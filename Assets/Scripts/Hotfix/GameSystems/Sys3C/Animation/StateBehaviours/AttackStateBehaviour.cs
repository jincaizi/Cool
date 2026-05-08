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

        private bool IsAttackState(AnimatorStateInfo stateInfo)
        {
            var hash = stateInfo.shortNameHash;
            return hash == HASH_Attack1 || hash == HASH_Attack2;
        }

        private string GetStateName(AnimatorStateInfo stateInfo)
        {
            var stateHash = stateInfo.shortNameHash;
            if (stateHash == HASH_Attack1) return "Attack1";
            if (stateHash == HASH_Attack2) return "Attack2";
            return "Unknown";
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (IsAttackState(stateInfo) && stateInfo.normalizedTime >= 0.95f && stateInfo.normalizedTime < 1.1f)
            {
                _onAnimationCompleted?.Invoke(GetStateName(stateInfo));
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (IsAttackState(stateInfo))
            {
                _onAnimationCompleted?.Invoke(GetStateName(stateInfo));
            }
        }
    }
}

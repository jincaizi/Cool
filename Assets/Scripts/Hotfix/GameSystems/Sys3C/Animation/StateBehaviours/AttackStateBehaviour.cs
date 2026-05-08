using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class AttackStateBehaviour : StateMachineBehaviour
    {
        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(Action<string> callback)
        {
            _onAnimationCompleted = callback;
        }

        private bool IsAttackState(AnimatorStateInfo stateInfo)
        {
            // Accept any non-default state on the attack layer
            return stateInfo.shortNameHash != 0;
        }

        private string GetStateName(AnimatorStateInfo stateInfo)
        {
            var hash = stateInfo.shortNameHash;
            // Return a generic tag for FSMManager to fire OnAttackAnimationCompleted
            return "AttackSkill";
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

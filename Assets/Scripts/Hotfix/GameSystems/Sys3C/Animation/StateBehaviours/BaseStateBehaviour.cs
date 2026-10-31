using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class BaseStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_JumpStart = Animator.StringToHash("JumpStart");
        private static readonly int HASH_JumpAir = Animator.StringToHash("JumpAir");
        private static readonly int HASH_JumpEnd = Animator.StringToHash("JumpEnd");

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
            if (stateInfo.shortNameHash == HASH_JumpEnd && stateInfo.normalizedTime >= 0.9f)
                entry.Attack?.Invoke("JumpEnd");
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }
    }
}
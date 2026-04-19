using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 挂在 Animator Controller 状态机上，监听状态进入/退出事件并转发给 CharacterAnimationDriver
    /// 替代 AnimationEvent，符合 Unity StateMachineBehaviour 官方推荐写法
    /// </summary>
    public class CharacterStateBehaviour : StateMachineBehaviour
    {
        /// <summary>
        /// 进入状态时的回调
        /// </summary>
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            var driver = animator.GetComponent<CharacterAnimationDriver>();
            if (driver == null) return;

            driver.OnStateEntered(stateInfo.shortNameHash);
        }

        /// <summary>
        /// 离开状态时的回调
        /// </summary>
        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            var driver = animator.GetComponent<CharacterAnimationDriver>();
            if (driver == null) return;

            driver.OnStateExited(stateInfo.shortNameHash);
        }
    }
}

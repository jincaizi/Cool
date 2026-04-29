using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    /// <summary>
    /// Base Layer 动画完成监听
    /// 监听 JumpStart、JumpAir、JumpEnd 动画事件
    /// </summary>
    public class BaseStateBehaviour : StateMachineBehaviour
    {
        // 状态哈希
        private static readonly int HASH_JumpStart = Animator.StringToHash("JumpStart");
        private static readonly int HASH_JumpAir = Animator.StringToHash("JumpAir");
        private static readonly int HASH_JumpEnd = Animator.StringToHash("JumpEnd");

        // 回调引用（由 FSMManager 设置）
        private static System.Action<string> _onAnimationCompleted;

        public static void SetCallback(System.Action<string> callback)
        {
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
            // JumpEnd 动画完成检测
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    Debug.Log("[BaseBehaviour] JumpEnd completed, normalizedTime=" + stateInfo.normalizedTime);
                    _onAnimationCompleted?.Invoke("JumpEnd");
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                Debug.Log("[BaseBehaviour] JumpEnd exited");
            }
        }
    }
}

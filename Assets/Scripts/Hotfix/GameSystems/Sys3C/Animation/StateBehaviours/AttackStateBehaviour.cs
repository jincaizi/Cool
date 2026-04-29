using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    /// <summary>
    /// Attack Layer 动画完成监听
    /// 监听 Attack1、Attack2 动画，处理连击窗口
    /// </summary>
    public class AttackStateBehaviour : StateMachineBehaviour
    {
        // 状态哈希
        private static readonly int HASH_Attack1 = Animator.StringToHash("Attack1");
        private static readonly int HASH_Attack2 = Animator.StringToHash("Attack2");

        // 连击窗口配置
        private const int COMBO_FRAME_LOCK = 5;        // 5帧后解锁连击
        private const float COMBO_WINDOW_START = 0.3f; // normalizedTime 开始
        private const float COMBO_WINDOW_END = 0.8f;    // normalizedTime 结束

        // 当前状态追踪
        private int _framesInState;
        private bool _comboUnlocked;

        // 回调引用
        private static System.Action<string> _onAnimationCompleted;

        public static void SetCallback(System.Action<string> callback)
        {
            _onAnimationCompleted = callback;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_Attack1 || stateInfo.shortNameHash == HASH_Attack2)
            {
                _framesInState = 0;
                _comboUnlocked = false;
                Debug.Log("[AttackBehaviour] " + stateInfo.shortNameHash + " entered");
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            _framesInState++;

            // 5帧后解锁连击
            if (!_comboUnlocked && _framesInState >= COMBO_FRAME_LOCK)
            {
                _comboUnlocked = true;
                Debug.Log("[AttackBehaviour] Combo unlocked at frame " + _framesInState);
            }

            // 检测动画完成
            if (stateInfo.shortNameHash == HASH_Attack1)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    Debug.Log("[AttackBehaviour] Attack1 completed");
                    _onAnimationCompleted?.Invoke("Attack1");
                }
            }
            else if (stateInfo.shortNameHash == HASH_Attack2)
            {
                if (stateInfo.normalizedTime >= 0.9f)
                {
                    Debug.Log("[AttackBehaviour] Attack2 completed");
                    _onAnimationCompleted?.Invoke("Attack2");
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            Debug.Log("[AttackBehaviour] " + stateInfo.shortNameHash + " exited");
        }
    }
}

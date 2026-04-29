using UnityEngine;
using System.Collections.Generic;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 挂在 Animator Controller 状态机上，监听 JumpEnd 动画完成事件
    ///
    /// 跳跃流程（即时响应）：
    /// 1. RequestJump() 立即应用跳跃力，角色立即开始上升
    /// 2. 播放 JumpStart 动画作为视觉效果
    /// 3. 代码控制 Y 轴（上升/下落）
    /// 4. 地面检测到落地，设置 JumpEnd，播放落地动画
    /// 5. JumpEnd 动画播放完成（通过此 Behavior），调用 OnJumpEndCompleted
    /// </summary>
    public class CharacterStateBehaviour : StateMachineBehaviour
    {
        private const float COMPLETE_THRESHOLD = 0.9f;

        private static readonly int HASH_JumpEnd = Animator.StringToHash("JumpEnd");

        // 每个角色独立追踪（避免静态共享导致多角色干扰）
        private static readonly Dictionary<int, bool> _jumpEndTriggeredPerAnimator = new Dictionary<int, bool>();

        public static event System.Action OnJumpEndCompletedEvent;

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                _jumpEndTriggeredPerAnimator[animator.GetInstanceID()] = false;
                UnityEngine.Debug.Log("[SMB] JumpEnd entered");
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                int id = animator.GetInstanceID();
                if (!_jumpEndTriggeredPerAnimator.TryGetValue(id, out var triggered) || !triggered)
                {
                    if (stateInfo.normalizedTime >= COMPLETE_THRESHOLD)
                    {
                        _jumpEndTriggeredPerAnimator[id] = true;
                        UnityEngine.Debug.Log("[SMB] JumpEnd completed, normalizedTime=" + stateInfo.normalizedTime.ToString("F3"));
                        OnJumpEndCompletedEvent?.Invoke();
                    }
                }
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.shortNameHash == HASH_JumpEnd)
            {
                _jumpEndTriggeredPerAnimator.Remove(animator.GetInstanceID());
                UnityEngine.Debug.Log("[SMB] JumpEnd exited");
            }
        }
    }
}

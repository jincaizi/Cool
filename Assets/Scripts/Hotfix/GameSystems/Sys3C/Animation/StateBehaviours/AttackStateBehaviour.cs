using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class AttackStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_Attack1 = Animator.StringToHash("Attack1");
        private static readonly int HASH_Attack2 = Animator.StringToHash("Attack2");
        private static readonly int HASH_SkillQ = Animator.StringToHash("AttackQ");  // SkillQ在Animator中叫AttackQ
        private static readonly int HASH_SkillR_Start = Animator.StringToHash("SkillR_Start");  // 新增
        private static readonly int HASH_SkillR_Loop = Animator.StringToHash("SkillR_Loop");      // 新增

        private static AnimationDriver _driver;
        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(AnimationDriver driver, Action<string> callback)
        {
            _driver = driver;
            _onAnimationCompleted = callback;
        }

        private bool IsAttackState(AnimatorStateInfo stateInfo)
        {
            var hash = stateInfo.shortNameHash;
            return hash == HASH_Attack1 || hash == HASH_Attack2 ||
                   hash == HASH_SkillQ ||
                   hash == HASH_SkillR_Start || hash == HASH_SkillR_Loop;
        }

        private string GetStateName(AnimatorStateInfo stateInfo)
        {
            var stateHash = stateInfo.shortNameHash;
            if (stateHash == HASH_Attack1) return "Attack1";
            if (stateHash == HASH_Attack2) return "Attack2";
            if (stateHash == HASH_SkillQ) return "AttackQ";
            if (stateHash == HASH_SkillR_Start) return "SkillR_Start";
            if (stateHash == HASH_SkillR_Loop) return "SkillR_Loop";
            return "Unknown";
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // 检查动画是否接近完成（normalizedTime >= 0.95），防止循环播放问题
            // SkillR_Loop是循环动画，不触发完成回调
            if (IsAttackState(stateInfo) && stateInfo.normalizedTime >= 0.95f && stateInfo.normalizedTime < 1.1f)
            {
                if (stateInfo.shortNameHash == HASH_SkillR_Loop) return;

                if (_onAnimationCompleted != null)
                {
                    _onAnimationCompleted.Invoke(GetStateName(stateInfo));
                }
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

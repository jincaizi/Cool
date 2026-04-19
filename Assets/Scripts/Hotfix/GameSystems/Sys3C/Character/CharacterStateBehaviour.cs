using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 挂在 Animator Controller 的各个状态上，监听状态进入/退出事件
    /// 替代 AnimationEvent，更符合 Unity 现代写法
    /// </summary>
    public class CharacterStateBehaviour : StateMachineBehaviour
    {
        // OnStateEnter: 进入状态时调用
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            var driver = animator.GetComponent<CharacterAnimationDriver>();
            if (driver == null) return;

            string stateName = stateInfo.shortNameHash.ToString();

            // 通过短名称哈希判断是哪个状态
            // 注意：Animtor.StringToHash("JumpStart") 获取状态的短名称哈希
            int hash = stateInfo.shortNameHash;

            // JumpStart 完成 → 进入 JumpAir
            if (hash == Animator.StringToHash("JumpStart"))
            {
                driver.OnJumpToAir();
            }
            // JumpEnd 完成 → 回到 Idle 或 BattleIdle
            else if (hash == Animator.StringToHash("JumpEnd"))
            {
                driver.OnJumpEndComplete();
            }
            // Attack 状态完成
            else if (hash == Animator.StringToHash("Attack1") ||
                     hash == Animator.StringToHash("Attack2") ||
                     hash == Animator.StringToHash("Attack3") ||
                     hash == Animator.StringToHash("Attack4"))
            {
                driver.OnAttackComplete();
            }
        }

        // OnStateExit: 离开状态时调用
        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // 可选：用于处理离开攻击状态时的逻辑
        }
    }
}

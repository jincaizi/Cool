using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class HitStateBehaviour : StateMachineBehaviour
    {
        // 动画状态Hash值（使用 Animator.StringToHash 预计算）
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");
        private static readonly int HASH_Knockback = Animator.StringToHash("Knockback");
        private static readonly int HASH_Launched = Animator.StringToHash("Launched");
        private static readonly int HASH_Dizzy = Animator.StringToHash("Dizzy");
        private static readonly int HASH_Down = Animator.StringToHash("Down");
        private static readonly int HASH_GetUp = Animator.StringToHash("GetUp");
        private static readonly int HASH_Death = Animator.StringToHash("Death");
        private static readonly int HASH_DefendHit = Animator.StringToHash("DefendHit");

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
            if (IsHitState(stateInfo.shortNameHash))
            {
                entry.HasTriggeredHitComplete = false;
                entry.LastNormalizedTime = 0f;
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
            int stateHash = stateInfo.shortNameHash;
            // 检查是否是受击相关状态
            if (IsHitState(stateHash) && !entry.HasTriggeredHitComplete)
            {
                // 检测动画播放到末尾（normalizedTime >= 0.9）
                if (stateInfo.normalizedTime >= 0.9f && stateInfo.normalizedTime > entry.LastNormalizedTime)
                {
                    entry.HasTriggeredHitComplete = true;
                    entry.Hit?.Invoke(GetStateName(stateHash));
                }
                entry.LastNormalizedTime = stateInfo.normalizedTime;
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // 动画结束后清理
            if (!AnimationCallbackRegistry.TryGet(animator, out var entry)) return;
            if (IsHitState(stateInfo.shortNameHash))
            {
                entry.HasTriggeredHitComplete = false;
                entry.LastNormalizedTime = 0f;
            }
        }

        private static bool IsHitState(int hash)
        {
            // N4：Death 也要报——HitFSM.OnAnimationEnd 有 case "Death"（打完死亡动画进躺卧），
            // 而 StateMachineBehaviour 上一次漏了它 ⇒ 那条分支在生产里永远收不到回调。
            // OnStateEnter 早已把 HASH_Death 算进受击态（同一份清单只缺这一处），层里也挂着本行为。
            return hash == HASH_Hit || hash == HASH_Knockback ||
                   hash == HASH_Launched || hash == HASH_Dizzy ||
                   hash == HASH_Down || hash == HASH_GetUp ||
                   hash == HASH_Death || hash == HASH_DefendHit;
        }

        private static string GetStateName(int hash)
        {
            if (hash == HASH_Hit) return "Hit";
            if (hash == HASH_Knockback) return "Knockback";
            if (hash == HASH_Launched) return "Launched";
            if (hash == HASH_Dizzy) return "Dizzy";
            if (hash == HASH_Down) return "Down";
            if (hash == HASH_GetUp) return "GetUp";
            if (hash == HASH_Death) return "Death";
            if (hash == HASH_DefendHit) return "DefendHit";
            return "Unknown";
        }
    }
}
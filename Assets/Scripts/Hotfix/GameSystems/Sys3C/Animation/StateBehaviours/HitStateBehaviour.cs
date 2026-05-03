using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.FSM;

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

        private static AnimationDriver _driver;
        private static Action<string> _onAnimationCompleted;

        // 防止重复触发的标记
        private static bool _hasTriggeredHitComplete;
        private static float _lastNormalizedTime;

        public static void SetCallback(AnimationDriver driver, Action<string> callback)
        {
            _driver = driver;
            _onAnimationCompleted = callback;
            _hasTriggeredHitComplete = false;
            _lastNormalizedTime = 0f;
        }

        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            var stateHash = stateInfo.shortNameHash;

            if (stateHash == HASH_Hit || stateHash == HASH_Knockback ||
                stateHash == HASH_Launched || stateHash == HASH_Dizzy ||
                stateHash == HASH_Down || stateHash == HASH_GetUp ||
                stateHash == HASH_Death)
            {
                _hasTriggeredHitComplete = false;
                _lastNormalizedTime = 0f;
                Debug.Log($"[HitBehaviour] State entered: {GetStateName(stateHash)}");
            }
        }

        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            var stateHash = stateInfo.shortNameHash;

            // 检查是否是受击相关状态
            if (IsHitState(stateHash) && !_hasTriggeredHitComplete)
            {
                // 检测动画播放到末尾（normalizedTime >= 0.9）
                if (stateInfo.normalizedTime >= 0.9f && stateInfo.normalizedTime > _lastNormalizedTime)
                {
                    _hasTriggeredHitComplete = true;
                    Debug.Log($"[HitBehaviour] State completed: {GetStateName(stateHash)}");
                    _onAnimationCompleted?.Invoke(GetStateName(stateHash));
                }
                _lastNormalizedTime = stateInfo.normalizedTime;
            }
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // 动画结束后清理
            if (IsHitState(stateInfo.shortNameHash))
            {
                _hasTriggeredHitComplete = false;
                _lastNormalizedTime = 0f;
            }
        }

        private static bool IsHitState(int hash)
        {
            return hash == HASH_Hit || hash == HASH_Knockback ||
                   hash == HASH_Launched || hash == HASH_Dizzy ||
                   hash == HASH_Down || hash == HASH_GetUp;
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
            return "Unknown";
        }
    }
}
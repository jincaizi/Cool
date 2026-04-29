using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    public class AttackStateBehaviour : StateMachineBehaviour
    {
        private static readonly int HASH_Attack1 = Animator.StringToHash("Attack1");
        private static readonly int HASH_Attack2 = Animator.StringToHash("Attack2");

        private const int COMBO_FRAME_LOCK = 5;
        private const float COMBO_WINDOW_START = 0.3f;
        private const float COMBO_WINDOW_END = 0.8f;

        private int _framesInState;
        private bool _comboUnlocked;

        private static AnimationDriver _driver;
        private static Action<string> _onAnimationCompleted;

        public static void SetCallback(AnimationDriver driver, Action<string> callback)
        {
            _driver = driver;
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

            if (!_comboUnlocked && _framesInState >= COMBO_FRAME_LOCK)
            {
                _comboUnlocked = true;
                Debug.Log("[AttackBehaviour] Combo unlocked at frame " + _framesInState);
            }

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

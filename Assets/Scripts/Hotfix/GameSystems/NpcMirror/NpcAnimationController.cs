using UnityEngine;

namespace Hotfix.GameSystems.NpcMirror
{
    public class NpcAnimationController
    {
        private readonly Animator _animator;
        private NpcAnimationState _currentState = NpcAnimationState.Idle;

        public NpcAnimationController(Animator animator)
        {
            _animator = animator;
        }

        public void SetState(NpcAnimationState state)
        {
            if (state == _currentState) return;
            _currentState = state;
            _animator?.SetInteger("NpcState", (int)state);
        }
    }
}

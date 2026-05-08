using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation
{
    public static class AnimHashes
    {
        public static readonly int BaseState = Animator.StringToHash("BaseState");
        public static readonly int AttackState = Animator.StringToHash("AttackState");
        public static readonly int HitState = Animator.StringToHash("HitState");
        public static readonly int IsJumping = Animator.StringToHash("IsJumping");
        public static readonly int IsHit = Animator.StringToHash("IsHit");
        public static readonly int IsDead = Animator.StringToHash("IsDead");
        public static readonly int Attack = Animator.StringToHash("Attack");
        public static readonly int Hit = Animator.StringToHash("Hit");
        public static readonly int Death = Animator.StringToHash("Death");
        public static readonly int Blend = Animator.StringToHash("Blend");

        public const int BaseLayerIndex = 0;
        public const int AttackLayerIndex = 1;
        public const int HitLayerIndex = 2;
    }
}

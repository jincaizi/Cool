using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // Single source of truth for all animator parameter hashes.
    // Each state class references these rather than duplicating hash strings.
    // Using static readonly int avoids repeated Animator.StringToHash calls.
    public static class MonsterAnimHashes
    {
        public static readonly int AIState     = Animator.StringToHash("AIState");
        public static readonly int Attack      = Animator.StringToHash("Attack");
        public static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
        public static readonly int Hit         = Animator.StringToHash("Hit");
        public static readonly int Death       = Animator.StringToHash("Death");
        public static readonly int Speed       = Animator.StringToHash("Speed");
        public static readonly int IsDefending = Animator.StringToHash("IsDefending");
        public static readonly int Taunt       = Animator.StringToHash("Taunt");
    }
}

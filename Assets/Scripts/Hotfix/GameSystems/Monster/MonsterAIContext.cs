using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Monster
{
    public struct MonsterAIContext
    {
        public Transform Self;
        public Transform Target;
        public Animator Animator;
        public MonsterStats Stats;
        public MonsterMovement Movement;
        public MonsterConfig Config;
        public float DeltaTime;
        public float StateTimer;
        public int CurrentAttackIndex;
        public bool AttackHitTarget;
        public IAttackShape AttackShape;
        public int DefendBlockCount;
        public float DefendChaseTimer;
    }
}

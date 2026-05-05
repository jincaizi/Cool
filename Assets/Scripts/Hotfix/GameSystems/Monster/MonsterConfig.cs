using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    [CreateAssetMenu(fileName = "MonsterConfig", menuName = "Game/Monster/Config")]
    public class MonsterConfig : ScriptableObject
    {
        [Header("Basic")]
        public string MonsterId;
        public string DisplayName;
        public GameObject Prefab;

        [Header("Stats")]
        public float MaxHP = 100;
        public float AttackPower = 20;
        public float Defense = 10;
        public float MoveSpeed = 3.5f;

        [Header("AI Ranges")]
        public float DetectRange = 10f;
        public float LeaveRange = 15f;
        public float AttackRange = 2f;
        public float AttackCooldown = 1.5f;

        [Header("Patrol")]
        public float PatrolRadius = 5f;
        public float IdleDuration = 2f;

        [Header("Combat")]
        public DamageData AttackDamage;
        public float KnockbackForce;
        public float HitStunDuration = 0.3f;

        [Header("Loot & Death")]
        public MonsterLootTable LootTable;
        public float DeathDestroyDelay = 3f;
    }
}

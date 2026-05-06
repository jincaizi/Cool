using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    [CreateAssetMenu(fileName = "MonsterConfig", menuName = "Game/Monster/Config")]
    public class MonsterConfig : ScriptableObject
    {
        [Header("Basic")]
        [Tooltip("怪物的唯一标识符，用于生成和寻路")]
        public string MonsterId;

        [Tooltip("怪物在UI中显示的名称")]
        public string DisplayName;

        [Tooltip("怪物预制体，必须包含 Animator、NavMeshAgent 等组件")]
        public GameObject Prefab;

        [Header("Stats")]
        [Tooltip("最大生命值")]
        public float MaxHP = 100;

        [Tooltip("基础攻击力")]
        public float AttackPower = 20;

        [Tooltip("防御力，用于减伤计算")]
        public float Defense = 10;

        [Tooltip("移动速度 (NavMeshAgent.speed)")]
        public float MoveSpeed = 3.5f;

        [Header("AI Ranges")]
        [Tooltip("检测范围，超过此距离开始追踪玩家")]
        public float DetectRange = 10f;

        [Tooltip("脱离战斗范围，超过此距离停止追踪")]
        public float LeaveRange = 15f;

        [Tooltip("攻击距离，接近此距离时发动攻击")]
        public float AttackRange = 2f;

        [Tooltip("攻击冷却时间 (秒)")]
        public float AttackCooldown = 1.5f;

        [Header("Patrol")]
        [Tooltip("巡逻半径，idle状态下的随机移动范围")]
        public float PatrolRadius = 5f;

        [Tooltip("idle状态持续时间 (秒)")]
        public float IdleDuration = 2f;

        [Header("Combat")]
        [Tooltip("每次攻击的伤害数据，包含伤害值、暴击率等")]
        public DamageData AttackDamage;

        [Tooltip("命中时的击退力")]
        public float KnockbackForce;

        [Tooltip("命中后硬直持续时间 (秒)")]
        public float HitStunDuration = 0.3f;

        [Header("Loot & Death")]
        [Tooltip("掉落表，决定死亡后掉落哪些物品")]
        public MonsterLootTable LootTable;

        [Tooltip("死亡动画完成后销毁延迟 (秒)")]
        public float DeathDestroyDelay = 3f;
    }
}

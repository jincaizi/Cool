using Hotfix.GameSystems.Skills.Data;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    [CreateAssetMenu(fileName = "MonsterConfig", menuName = "Game/Monster/Config")]
    public class MonsterConfig : ScriptableObject
    {
        [Header("Basic")]
        // Unique identifier for the monster
        [Tooltip("怪物的唯一标识符")]
        public string MonsterId;
        // Display name of the monster
        [Tooltip("怪物显示名称")]
        public string DisplayName;
        // Monster prefab
        [Tooltip("怪物预制体")]
        public GameObject Prefab;

        [Header("Stats")]
        // Maximum health points
        [Tooltip("最大生命值")]
        public float MaxHP = 100;
        // Attack power
        [Tooltip("攻击力")]
        public float AttackPower = 20;
        // Defense
        [Tooltip("防御力")]
        public float Defense = 10;
        // Movement speed
        [Tooltip("移动速度")]
        public float MoveSpeed = 3.5f;

        [Header("AI Ranges")]
        // Range to detect player
        [Tooltip("检测玩家的范围")]
        public float DetectRange = 10f;
        // Range to leave combat
        [Tooltip("脱离战斗的范围")]
        public float LeaveRange = 15f;
        // Attack range
        [Tooltip("攻击距离")]
        public float AttackRange = 2f;
        // Attack cooldown in seconds
        [Tooltip("攻击冷却时间(秒)")]
        public float AttackCooldown = 1.5f;
        // Attack cooldown random variance
        [Tooltip("攻击冷却随机变化量")]
        public float AttackCooldownVariance = 0.3f;

        [Header("Patrol")]
        // Patrol radius
        [Tooltip("巡逻半径")]
        public float PatrolRadius = 5f;
        // Patrol radius random variance
        [Tooltip("巡逻半径随机变化量")]
        public float PatrolRadiusVariance = 1f;
        // Idle state duration in seconds
        [Tooltip("Idle状态持续时间(秒)")]
        public float IdleDuration = 2f;
        // Idle duration random variance
        [Tooltip("Idle持续时间随机变化量")]
        public float IdleDurationVariance = 0.5f;

        [Header("Attack")]
        // Number of available attack animations
        [Tooltip("可用攻击动画数量")]
        public int AttackAnimCount = 1;
        // Random weights for each attack animation
        [Tooltip("各攻击动画的随机权重")]
        public float[] AttackWeights = { 1f };
        // Attack animation speed
        [Tooltip("攻击动画速度")]
        public float AttackAnimSpeed = 1f;

        [Header("Attack Shape")]
        // Attack shape configuration
        [Tooltip("攻击判定形状配置")]
        public ShapeBlock AttackShape;

        [Header("Attack Damage")]
        [Tooltip("攻击伤害配置")]
        public DamageBlock AttackDamage;

        [Header("Attack Effect")]
        [Tooltip("攻击效果配置")]
        public EffectBlock AttackEffect;

        [Header("Defend")]
        // Whether to enable defend behavior (TurtleShell)
        [Tooltip("是否启用防御行为(TurtleShell)")]
        public bool EnableDefend;
        // Trigger defend when HP below this ratio
        [Tooltip("HP低于此比例触发防御")]
        public float DefendHPThreshold = 0.5f;
        // Trigger defend after chasing for this time in seconds
        [Tooltip("追击超过此时间触发防御(秒)")]
        public float DefendChaseTimeThreshold = 3f;
        // Defend duration in seconds
        [Tooltip("防御持续时间(秒)")]
        public float DefendDuration = 2f;
        // Front damage reduction ratio (0-1)
        [Tooltip("正面减伤比例(0-1)")]
        public float DefendDamageReduction = 0.8f;
        // Effective defend angle
        [Tooltip("有效防御角度")]
        public float DefendAngle = 180f;
        // Number of blocks before counter attack
        [Tooltip("格挡N次后触发反击")]
        public int DefendBlockCountToCounter = 2;
        // Counter attack damage multiplier
        [Tooltip("反击伤害倍率")]
        public float DefendCounterDamageMultiplier = 1.5f;
        // Defend cooldown in seconds
        [Tooltip("防御冷却时间(秒)")]
        public float DefendCooldown = 8f;

        [Header("Taunt")]
        // Whether to enable taunt behavior (Slime)
        [Tooltip("是否启用嘲讽行为(Slime)")]
        public bool EnableTaunt;
        // Chance to trigger taunt after missed attack
        [Tooltip("攻击落空后触发嘲讽的概率")]
        public float TauntChance = 0.6f;
        // Taunt animation duration in seconds
        [Tooltip("嘲讽动画持续时间(秒)")]
        public float TauntDuration = 1.5f;

        [Header("Alert")]
        // Alert detection range
        [Tooltip("警戒感知距离")]
        public float AlertRange = 15f;

        [Header("Movement")]
        // Use run animation when chasing
        [Tooltip("追击时使用跑步动画")]
        public bool ChaseAnimIsRun = true;
        // Rotation speed
        [Tooltip("转身速度")]
        public float RotationSpeed = 10f;

        [Header("Spawn")]
        // Spawn mode: RandomArea=random in area, FixedPoints=fixed points
        [Tooltip("刷新模式: RandomArea=区域内随机, FixedPoints=固定点位")]
        public SpawnMode SpawnMode;
        // Fixed spawn positions (only for FixedPoints mode)
        [Tooltip("固定刷新点位(仅 FixedPoints 模式)")]
        public Vector3[] FixedSpawnPositions;

        [Header("Loot & Death")]
        // Loot table
        [Tooltip("掉落表")]
        public MonsterLootTable LootTable;
        // Destroy delay after death in seconds
        [Tooltip("死亡后销毁延迟(秒)")]
        public float DeathDestroyDelay = 3f;

        [Header("Selection Ring")]
        [Tooltip("选中光环的脚底Y轴偏移量，用于调整光环在目标脚下的高度位置")]
        public float RingYOffset = -0.9f;
    }

    public enum SpawnMode
    {
        RandomArea = 0,
        FixedPoints = 1,
    }
}
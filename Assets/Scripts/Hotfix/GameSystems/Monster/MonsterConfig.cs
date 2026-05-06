using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Monster
{
    [CreateAssetMenu(fileName = "MonsterConfig", menuName = "Game/Monster/Config")]
    public class MonsterConfig : ScriptableObject
    {
        [Header("Basic")]
        [Tooltip("怪物的唯一标识符")]
        public string MonsterId;
        [Tooltip("怪物显示名称")]
        public string DisplayName;
        [Tooltip("怪物预制体")]
        public GameObject Prefab;

        [Header("Stats")]
        [Tooltip("最大生命值")]
        public float MaxHP = 100;
        [Tooltip("攻击力")]
        public float AttackPower = 20;
        [Tooltip("防御力")]
        public float Defense = 10;
        [Tooltip("移动速度")]
        public float MoveSpeed = 3.5f;

        [Header("AI Ranges")]
        [Tooltip("检测玩家的范围")]
        public float DetectRange = 10f;
        [Tooltip("脱离战斗的范围")]
        public float LeaveRange = 15f;
        [Tooltip("攻击距离")]
        public float AttackRange = 2f;
        [Tooltip("攻击冷却时间(秒)")]
        public float AttackCooldown = 1.5f;
        [Tooltip("攻击冷却随机变化量")]
        public float AttackCooldownVariance = 0.3f;

        [Header("Patrol")]
        [Tooltip("巡逻半径")]
        public float PatrolRadius = 5f;
        [Tooltip("巡逻半径随机变化量")]
        public float PatrolRadiusVariance = 1f;
        [Tooltip("Idle状态持续时间(秒)")]
        public float IdleDuration = 2f;
        [Tooltip("Idle持续时间随机变化量")]
        public float IdleDurationVariance = 0.5f;

        [Header("Attack")]
        [Tooltip("可用攻击动画数量")]
        public int AttackAnimCount = 1;
        [Tooltip("各攻击动画的随机权重")]
        public float[] AttackWeights = { 1f };
        [Tooltip("攻击动画速度")]
        public float AttackAnimSpeed = 1f;

        [Header("Attack Shape")]
        [Tooltip("攻击判定形状配置")]
        public AttackShapeConfig AttackShape;

        [Header("Attack Effects")]
        [Tooltip("每个攻击变体的效果列表")]
        public AttackEffectConfig[] AttackEffects;

        [Header("Defend")]
        [Tooltip("是否启用防御行为(TurtleShell)")]
        public bool EnableDefend;
        [Tooltip("HP低于此比例触发防御")]
        public float DefendHPThreshold = 0.5f;
        [Tooltip("追击超过此时间触发防御(秒)")]
        public float DefendChaseTimeThreshold = 3f;
        [Tooltip("防御持续时间(秒)")]
        public float DefendDuration = 2f;
        [Tooltip("正面减伤比例(0-1)")]
        public float DefendDamageReduction = 0.8f;
        [Tooltip("有效防御角度")]
        public float DefendAngle = 180f;
        [Tooltip("格挡N次后触发反击")]
        public int DefendBlockCountToCounter = 2;
        [Tooltip("反击伤害倍率")]
        public float DefendCounterDamageMultiplier = 1.5f;
        [Tooltip("防御冷却时间(秒)")]
        public float DefendCooldown = 8f;

        [Header("Taunt")]
        [Tooltip("是否启用嘲讽行为(Slime)")]
        public bool EnableTaunt;
        [Tooltip("攻击落空后触发嘲讽的概率")]
        public float TauntChance = 0.6f;
        [Tooltip("嘲讽动画持续时间(秒)")]
        public float TauntDuration = 1.5f;

        [Header("Alert")]
        [Tooltip("警戒感知距离")]
        public float AlertRange = 15f;

        [Header("Movement")]
        [Tooltip("追击时使用跑步动画")]
        public bool ChaseAnimIsRun = true;
        [Tooltip("转身速度")]
        public float RotationSpeed = 10f;

        [Header("Spawn")]
        [Tooltip("刷新模式: RandomArea=区域内随机, FixedPoints=固定点位")]
        public SpawnMode SpawnMode;
        [Tooltip("固定刷新点位(仅 FixedPoints 模式)")]
        public Vector3[] FixedSpawnPositions;

        [Header("Loot & Death")]
        [Tooltip("掉落表")]
        public MonsterLootTable LootTable;
        [Tooltip("死亡后销毁延迟(秒)")]
        public float DeathDestroyDelay = 3f;
    }

    public enum SpawnMode
    {
        RandomArea = 0,
        FixedPoints = 1,
    }
}

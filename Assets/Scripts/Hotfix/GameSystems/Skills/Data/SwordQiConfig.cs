using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "SwordQiConfig", menuName = "Game/Skills/Sword Qi Config")]
    public class SwordQiConfig : ScriptableObject
    {
        [Header("=== Projectile ===")]
        [Tooltip("剑气预制体（根物体需含 SwordQiProjectile 组件）")]
        [SerializeField] private GameObject _prefab;
        public GameObject Prefab => _prefab;

        [Tooltip("飞行速度(米/秒)")]
        [SerializeField] private float _speed = 18f;
        public float Speed => _speed;

        [Tooltip("射程(米)，累计飞行距离耗尽后回池")]
        [SerializeField] private float _range = 12f;
        public float Range => _range;

        [Tooltip("判定带半径(米)，宽扁带判定，兼作高度差补偿")]
        [SerializeField] private float _detectRadius = 1f;
        public float DetectRadius => _detectRadius;

        [Tooltip("伤害倍率 (1.0 = 继承技能全额伤害)")]
        [SerializeField] private float _damagePercent = 1f;
        public float DamagePercent => _damagePercent;

        [Tooltip("判定帧数组中的第几帧生成剑气 (0 = 第一判定帧)")]
        [SerializeField] private int _spawnFrameIndex;
        public int SpawnFrameIndex => _spawnFrameIndex;

        [Tooltip("生成位置偏移（角色局部坐标：前方 + 剑高）")]
        [SerializeField] private Vector3 _spawnOffset = new Vector3(0.8f, 1.0f, 0.6f);
        public Vector3 SpawnOffset => _spawnOffset;

        [Tooltip("命中击退等效果（KnockbackForce > 0 时覆盖技能默认；当前仅击退生效）")]
        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;
    }
}

using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "ProjectileSkill", menuName = "Game/Skills/Projectile Skill")]
    public class ProjectileSkillData : SkillData
    {
        [Header("=== Projectile ===")]
        [Tooltip("施放时生成的投射物预制体")]
        [SerializeField] private GameObject _projectilePrefab;
        public GameObject ProjectilePrefab => _projectilePrefab;

        [Tooltip("投射物速度(世界单位/秒)")]
        [SerializeField] private float _projectileSpeed = 20f;
        public float ProjectileSpeed => _projectileSpeed;

        [Tooltip("投射物最大飞行射程(米)，耗尽后回池")]
        [SerializeField] private float _projectileRange = 15f;
        public float ProjectileRange => _projectileRange;

        [Tooltip("投射物是否穿透目标?")]
        [SerializeField] private bool _projectilePierce;
        public bool ProjectilePierce => _projectilePierce;

        [Tooltip("投射物可以穿透的最大目标数")]
        [SerializeField] private int _maxPierceTargets = 3;
        public int MaxPierceTargets => _maxPierceTargets;

        [Tooltip("投射物是否追踪目标?")]
        [SerializeField] private bool _homing;
        public bool Homing => _homing;

        [Tooltip("生成位置偏移（角色局部坐标：前方 + 胸前高度）")]
        [SerializeField] private Vector3 _spawnOffset = new Vector3(0f, 1.2f, 0.5f);
        public Vector3 SpawnOffset => _spawnOffset;

        [Tooltip("命中首个目标后的爆炸半径（0 = 仅单体，不产生范围伤害）")]
        [SerializeField] private float _explosionRadius = 1.5f;
        public float ExplosionRadius => _explosionRadius;

        [Header("=== Charge (Aim & Throw) ===")]
        [Tooltip("蓄力最小时间(秒)")]
        [SerializeField] private float _minChargeTime = 0.3f;
        public float MinChargeTime => _minChargeTime;

        [Tooltip("蓄力最大时间(秒)，0 = 非蓄力投射物（按下即发射）")]
        [SerializeField] private float _maxChargeTime = 2f;
        public float MaxChargeTime => _maxChargeTime;

        [Tooltip("蓄力阶段是否可以移动?")]
        [SerializeField] private bool _canMoveWhileCharging = true;
        public bool CanMoveWhileCharging => _canMoveWhileCharging;

        [Tooltip("蓄力阶段是否可以旋转?")]
        [SerializeField] private bool _canRotateWhileCharging = true;
        public bool CanRotateWhileCharging => _canRotateWhileCharging;

        [Tooltip("弹道重力加速度(米/秒²)")]
        [SerializeField] private float _projectileGravity = 9.8f;
        public float ProjectileGravity => _projectileGravity;

        [Header("=== Config Blocks ===")]
        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;
        // Note: no ShapeBlock — projectile handles its own collision detection

        private void OnValidate()
        {
            _skillType = Definition.SkillType.Projectile;
        }
    }
}

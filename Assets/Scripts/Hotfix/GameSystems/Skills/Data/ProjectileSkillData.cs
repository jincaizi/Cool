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

        [Tooltip("投射物是否穿透目标?")]
        [SerializeField] private bool _projectilePierce;
        public bool ProjectilePierce => _projectilePierce;

        [Tooltip("投射物可以穿透的最大目标数")]
        [SerializeField] private int _maxPierceTargets = 3;
        public int MaxPierceTargets => _maxPierceTargets;

        [Tooltip("投射物是否追踪目标?")]
        [SerializeField] private bool _homing;
        public bool Homing => _homing;

        [Header("=== Config Blocks ===")]
        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;
        // Note: no ShapeBlock — projectile handles its own collision detection
    }
}

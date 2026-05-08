using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    public enum TargetType
    {
        [Tooltip("单体目标，射线或锁定检测")]
        Single,
        [Tooltip("圆形AOE")]
        AOE_Circle,
        [Tooltip("锥形AOE")]
        AOE_Cone,
        [Tooltip("扇形AOE")]
        AOE_Sector,
        [Tooltip("以自身为中心")]
        Self
    }

    [System.Serializable]
    public class ShapeBlock
    {
        [Header("=== Target ===")]
        [Tooltip("打击目标类型")]
        [SerializeField] private TargetType _targetType = TargetType.Single;
        public TargetType TargetType => _targetType;

        [Header("=== Dimensions ===")]
        [Tooltip("攻击范围（距离/半径）")]
        [SerializeField] private float _range = 2f;
        public float Range => _range;

        [Tooltip("锥形角度（仅 Cone 有效）")]
        [SerializeField] private float _angle = 120f;
        public float Angle => _angle;

        [Tooltip("扇形起始角度（仅 Sector 有效）")]
        [SerializeField] private float _angleStart;
        public float AngleStart => _angleStart;

        [Tooltip("扇形终止角度（仅 Sector 有效）")]
        [SerializeField] private float _angleEnd = 90f;
        public float AngleEnd => _angleEnd;

        [Tooltip("AOE半径（世界单位，0 = 单体）")]
        [SerializeField] private float _areaRadius;
        public float AreaRadius => _areaRadius;

        [Tooltip("矩形宽度（仅 Rect 有效）")]
        [SerializeField] private float _width = 1f;
        public float Width => _width;

        [Header("=== Collision ===")]
        [Tooltip("碰到第一个目标后是否停止")]
        [SerializeField] private bool _stopAtFirst;
        public bool StopAtFirst => _stopAtFirst;

        [Tooltip("目标检测的物理层遮罩")]
        [SerializeField] private LayerMask _targetMask = ~0;
        public LayerMask TargetMask => _targetMask;

        [Header("=== Hit Timings ===")]
        [Tooltip("从技能开始算起的判定帧时间（秒）。每个值是一次独立的伤害检测。")]
        [SerializeField] private float[] _hitboxTimings = new float[] { 0.3f };
        public float[] HitboxTimings => _hitboxTimings;
    }
}

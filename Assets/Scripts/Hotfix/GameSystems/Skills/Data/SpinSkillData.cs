using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "SpinSkill", menuName = "Game/Skills/Spin Skill")]
    public class SpinSkillData : SkillData
    {
        [Header("=== Spin Duration ===")]
        [Tooltip("最低持续时长(秒)。该时段内再按技能键无效")]
        [SerializeField] private float _minDuration = 1f;
        public float MinDuration => _minDuration;

        [Tooltip("最大持续时长(秒)。到点自动结束")]
        [SerializeField] private float _maxDuration = 5f;
        public float MaxDuration => _maxDuration;

        [Header("=== Damage Ticks ===")]
        [Tooltip("伤害结算间隔(秒)。第一个tick在起手动画结束后一个间隔")]
        [SerializeField] private float _tickInterval = 0.2f;
        public float TickInterval => _tickInterval;

        [Tooltip("单目标最大命中次数(<=0 = 无上限)")]
        [SerializeField] private int _maxHitsPerTarget = 5;
        public int MaxHitsPerTarget => _maxHitsPerTarget;

        [Header("=== Movement ===")]
        [Tooltip("旋转期间移动速度倍率(0-1)")]
        [SerializeField] private float _moveSpeedMultiplier = 0.5f;
        public float MoveSpeedMultiplier => _moveSpeedMultiplier;

        [Header("=== Config Blocks ===")]
        [SerializeField] private ShapeBlock _shape;
        public ShapeBlock Shape => _shape;

        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;

        /// <summary>
        /// 取消窗口：elapsed 在 [MinDuration, MaxDuration) 内可取消
        /// </summary>
        public bool IsInCancelWindow(float elapsed)
        {
            return elapsed >= _minDuration && elapsed < _maxDuration;
        }

        private void OnValidate()
        {
            _skillType = Definition.SkillType.Spin;
            _tickInterval = Mathf.Max(0.01f, _tickInterval);
            _maxDuration = Mathf.Max(_minDuration, _maxDuration);
            _moveSpeedMultiplier = Mathf.Clamp01(_moveSpeedMultiplier);

            if (_castClip != null && _maxDuration <= _castClip.length)
            {
                Debug.LogWarning($"[SpinSkillData] {name}: MaxDuration({_maxDuration}) <= 起手动画时长({_castClip.length})，持续期间不会有任何伤害tick");
            }
        }
    }
}

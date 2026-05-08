using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "ChanneledSkill", menuName = "Game/Skills/Channeled Skill")]
    public class ChanneledSkillData : SkillData
    {
        [Header("=== Channel ===")]
        [Tooltip("技能释放前的引导时间(秒)")]
        [SerializeField] private float _castTime;
        public float CastTime => _castTime;

        [Tooltip("引导持续时间(秒)")]
        [SerializeField] private float _channelDuration;
        public float ChannelDuration => _channelDuration;

        [Tooltip("引导动画片段")]
        [SerializeField] private AnimationClip _channelClip;
        public AnimationClip ChannelClip => _channelClip;

        [Tooltip("引导期间伤害检测的间隔秒数")]
        [SerializeField] private float _tickInterval = 1f;
        public float TickInterval => _tickInterval;

        [Tooltip("每次检测的基础伤害百分比 (0-1)")]
        [SerializeField][Range(0f, 1f)] private float _tickDamagePercent = 0.2f;
        public float TickDamagePercent => _tickDamagePercent;

        [Tooltip("引导效果是否跟随目标移动?")]
        [SerializeField] private bool _channelFollowsTarget;
        public bool ChannelFollowsTarget => _channelFollowsTarget;

        [Tooltip("目标移出范围时中断引导?")]
        [SerializeField] private bool _breakOnTargetMove;
        public bool BreakOnTargetMove => _breakOnTargetMove;

        [Header("=== Movement ===")]
        [Tooltip("引导阶段是否可以移动?")]
        [SerializeField] private bool _canMoveWhileChanneling = true;
        public bool CanMoveWhileChanneling => _canMoveWhileChanneling;

        [Header("=== Config Blocks ===")]
        [SerializeField] private ShapeBlock _shape;
        public ShapeBlock Shape => _shape;

        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;

        public int GetTotalChannelTicks()
        {
            if (_channelDuration <= 0 || _tickInterval <= 0) return 0;
            return Mathf.FloorToInt(_channelDuration / _tickInterval);
        }

        private void OnValidate()
        {
            _skillType = Definition.SkillType.Channeled;
        }
    }
}

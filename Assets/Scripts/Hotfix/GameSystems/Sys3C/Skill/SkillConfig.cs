using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C.Skill
{
    /// <summary>
    /// 技能配置（ScriptableObject）
    /// </summary>
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "Game/Skill")]
    public class SkillConfig : ScriptableObject
    {
        [Header("Basic Info")]
        public string SkillName;
        public string SkillId;

        [Header("Animation")]
        public string AnimationName;      // 动画名（如 "AttackQ"）

        [Header("Cooldown")]
        public float Cooldown;            // CD时间（秒），0表示无CD

        [Header("Usage Condition")]
        public bool CanUseInAir = true;    // 是否可空中使用

        [Header("Duration Skill")]
        public float MaxDuration = 3f;     // 最大持续时长（秒），0表示无限制

        [Header("Combo")]
        public float ComboWindowStart;     // 连击窗口开始（normalizedTime）
        public float ComboWindowEnd;       // 连击窗口结束（normalizedTime）
        public int ComboFrameLock;         // 固定帧解锁，0表示无连击

        [Header("Attack")]
        public AttackShapeConfig AttackShape;
        public AttackEffectConfig[] Effects;

        [Header("Execution")]
        public ExecutePattern Pattern;
        public MoveBehaviour MoveLock;
        public TargetingMode Targeting;

        [Header("Dash")]
        public float DashDistance;
        public float DashDuration;

        [Header("Pulse")]
        public float PulseInterval;
        public float PulseDuration;
    }

    public enum ExecutePattern { Instant, Pulse, Channel, Combo }
    public enum MoveBehaviour { Root, Free, Dash }
    public enum TargetingMode { Forward, Self, Target, Ground }
}

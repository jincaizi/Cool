using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    [CreateAssetMenu(menuName = "Game/HitFeedbackProfile")]
    public class HitFeedbackProfile : ScriptableObject
    {
        [Header("=== HitStop (Animator Freeze) ===")]
        [Tooltip("普通攻击 hitstop 时长(秒)")]
        public float NormalHitStop = 0.03f;

        [Tooltip("技能命中 hitstop 时长(秒)")]
        public float SkillHitStop = 0.08f;

        [Tooltip("暴击额外 hitstop 时长(秒)")]
        public float CritHitStopBonus = 0.04f;

        [Tooltip("hitstop 最大时长上限(秒)")]
        public float MaxHitStop = 0.15f;

        [Tooltip("连击段数加成 (每段 +N 秒)")]
        public float ComboHitStopBonus = 0.01f;

        [Header("=== Camera Shake ===")]
        [Tooltip("普通攻击震动强度")]
        public float NormalShakeIntensity = 0.5f;

        [Tooltip("技能命中震动强度")]
        public float SkillShakeIntensity = 1.5f;

        [Tooltip("暴击震动倍率")]
        public float CritShakeMultiplier = 1.5f;

        [Tooltip("震动持续时间(秒)")]
        public float ShakeDuration = 0.15f;

        [Header("=== Time Slow (Crit / Full Charge) ===")]
        [Tooltip("暴击时时间缩放 (0.3 = 30% 速度)")]
        public float CritTimeSlowScale = 0.3f;

        [Tooltip("暴击慢动作持续时间(秒)")]
        public float CritTimeSlowDuration = 0.3f;

        [Header("=== Particle Intensity ===")]
        [Tooltip("普通攻击粒子缩放")]
        public float NormalParticleScale = 1.0f;

        [Tooltip("技能命中粒子缩放")]
        public float SkillParticleScale = 1.5f;

        [Tooltip("暴击粒子缩放")]
        public float CritParticleScale = 2.0f;
    }
}

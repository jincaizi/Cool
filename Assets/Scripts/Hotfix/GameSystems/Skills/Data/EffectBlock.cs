using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Skills.Data
{
    [System.Serializable]
    public class EffectBlock
    {
        [Header("=== On-Hit Effects ===")]
        [Tooltip("命中时施加的状态效果 (buff, debuff, stun, knockback等)")]
        [SerializeField] private EffectData[] _applyEffects;
        public EffectData[] ApplyEffects => _applyEffects;

        [Header("=== Force ===")]
        [Tooltip("击退力度")]
        [SerializeField] private float _knockbackForce;
        public float KnockbackForce => _knockbackForce;

        [Tooltip("浮空力度")]
        [SerializeField] private float _launchForce;
        public float LaunchForce => _launchForce;

        [Header("=== Status ===")]
        [Tooltip("硬直持续时间 (秒)")]
        [SerializeField] private float _stunDuration;
        public float StunDuration => _stunDuration;

        [Tooltip("附带的状态效果类型")]
        [SerializeField] private StatusEffectType _statusType;
        public StatusEffectType StatusType => _statusType;

        [Tooltip("状态效果持续时间 (秒)")]
        [SerializeField] private float _statusDuration;
        public float StatusDuration => _statusDuration;

        [Tooltip("状态效果数值 (中毒伤害/减速百分比等)")]
        [SerializeField] private float _statusValue;
        public float StatusValue => _statusValue;
    }

    public enum StatusEffectType
    {
        None = 0,
        Poison = 1,
        Bleed = 2,
        Slow = 3,
        Stun = 4,
    }
}

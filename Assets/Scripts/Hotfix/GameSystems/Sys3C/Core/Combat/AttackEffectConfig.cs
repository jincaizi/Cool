using System;
using UnityEngine;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum StatusEffectType
    {
        None = 0,
        Poison = 1,
        Bleed = 2,
        Slow = 3,
        Stun = 4,
    }

    [Serializable]
    public class AttackEffectConfig
    {
        [Header("Damage")]
        // Damage data: base damage, attribute scaling, crit, etc.
        [Tooltip("攻击造成的伤害数据，包含基础伤害、属性缩放、暴击等")]
        public DamageBlock Damage;

        [Header("Force")]
        // Knockback force
        [Tooltip("击退力度")]
        public float KnockbackForce;

        // Launch force (airborne)
        [Tooltip("浮空力度")]
        public float LaunchForce;

        [Header("Status")]
        // Stun duration in seconds
        [Tooltip("硬直持续时间 (秒)")]
        public float StunDuration;

        // Status effect type applied
        [Tooltip("附带的状态效果类型")]
        public StatusEffectType Status;

        // Status effect duration in seconds
        [Tooltip("状态效果持续时间 (秒)")]
        public float StatusDuration;

        // Status effect value (poison damage / slow percentage, etc.)
        [Tooltip("状态效果数值 (中毒伤害/减速百分比等)")]
        public float StatusValue;
    }
}
using System;
using UnityEngine;
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
        [Tooltip("攻击造成的伤害数据，包含基础伤害、属性缩放、暴击等")]
        public DamageData Damage;

        [Header("Force")]
        [Tooltip("击退力度")]
        public float KnockbackForce;

        [Tooltip("浮空力度")]
        public float LaunchForce;

        [Header("Status")]
        [Tooltip("硬直持续时间 (秒)")]
        public float StunDuration;

        [Tooltip("附带的状态效果类型")]
        public StatusEffectType Status;

        [Tooltip("状态效果持续时间 (秒)")]
        public float StatusDuration;

        [Tooltip("状态效果数值 (中毒伤害/减速百分比等)")]
        public float StatusValue;
    }
}

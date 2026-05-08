using System;
using Hotfix.GameSystems.Skills.Data;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 攻击碰撞箱数据
    /// </summary>
    [Serializable]
    public class AttackHitboxData
    {
        /// <summary>
        /// 伤害数据
        /// </summary>
        public DamageBlock DamageData;

        /// <summary>
        /// 击退力
        /// </summary>
        public float KnockbackForce;

        /// <summary>
        /// 击飞力
        /// </summary>
        public float LaunchForce;

        /// <summary>
        /// 眩晕持续时间
        /// </summary>
        public float StunDuration;

        /// <summary>
        /// 是否暴击
        /// </summary>
        public bool IsCritical;

        /// <summary>
        /// 攻击来源ID
        /// </summary>
        public int SourceId;
    }
}

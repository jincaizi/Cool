using System;
using Hotfix.GameSystems.Skills.Effect;

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
        public DamageData DamageData;

        /// <summary>
        /// 击退力
        /// </summary>
        public float KnockbackForce;
    }
}
using UnityEngine;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 可受伤害对象接口
    /// </summary>
    public interface IDamageable
    {
        /// <summary>
        /// 接受伤害
        /// </summary>
        void TakeDamage(DamageBlock damageData, Vector3 hitDirection);

        /// <summary>
        /// 是否存活
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// 变换组件引用
        /// </summary>
        Transform Transform { get; }
    }
}
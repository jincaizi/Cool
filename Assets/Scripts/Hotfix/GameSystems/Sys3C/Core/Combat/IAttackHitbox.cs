using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 攻击碰撞箱接口
    /// </summary>
    public interface IAttackHitbox
    {
        /// <summary>
        /// 是否已激活
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// 当前攻击碰撞箱数据
        /// </summary>
        AttackHitboxData CurrentData { get; }

        /// <summary>
        /// 实例 ID，用于 HitZone 去重
        /// </summary>
        int GetInstanceID();

        /// <summary>
        /// 激活攻击碰撞箱
        /// </summary>
        void Activate(DamageData damageData);

        /// <summary>
        /// 停用攻击碰撞箱
        /// </summary>
        void Deactivate();

        /// <summary>
        /// 触发攻击命中
        /// </summary>
        void TriggerHit();

        /// <summary>
        /// 碰撞检测的世界坐标范围
        /// </summary>
        Bounds GetBounds();
    }
}

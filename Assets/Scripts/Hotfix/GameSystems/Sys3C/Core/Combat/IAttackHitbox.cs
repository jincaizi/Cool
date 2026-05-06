using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 攻击碰撞箱接口
    /// </summary>
    public interface IAttackHitbox
    {
        /// <summary>
        /// 触发攻击命中
        /// </summary>
        void TriggerHit(AttackHitboxData hitData);

        /// <summary>
        /// 是否已激活
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// 碰撞检测的世界坐标范围
        /// </summary>
        Bounds GetBounds();
    }
}
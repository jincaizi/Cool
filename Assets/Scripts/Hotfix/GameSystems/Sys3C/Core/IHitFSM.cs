using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// HitFSM 接口 - 用于跨程序集解耦
    /// </summary>
    public interface IHitFSM
    {
        /// <summary>
        /// 当前受击状态
        /// </summary>
        int CurrentState { get; }

        /// <summary>
        /// 是否处于霸体状态
        /// </summary>
        bool HasSuperArmor { get; }

        /// <summary>
        /// 获取受击位移
        /// </summary>
        Vector3 GetKnockbackDisplacement();

        /// <summary>
        /// 获取击退方向
        /// </summary>
        Vector3 GetHitDirection();
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 攻击形状接口
    /// </summary>
    public interface IAttackShape
    {
        /// <summary>
        /// 解析攻击形状，返回命中目标列表
        /// </summary>
        IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask);
    }
}

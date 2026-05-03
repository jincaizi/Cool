using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 状态协调器接口 - 用于测试和模块解耦
    /// </summary>
    public interface IStateCoordinator
    {
        /// <summary>
        /// 当前活跃层
        /// </summary>
        LayerType ActiveLayer { get; }

        /// <summary>
        /// 是否可以移动
        /// </summary>
        bool CanMove { get; }

        /// <summary>
        /// 是否可以攻击
        /// </summary>
        bool CanAttack { get; }

        /// <summary>
        /// 是否有霸体
        /// </summary>
        bool HasSuperArmor { get; }

        /// <summary>
        /// 是否处于免疫状态（死亡）
        /// </summary>
        bool IsImmune { get; }

        /// <summary>
        /// 获取韧性值
        /// </summary>
        float GetResistance();

        /// <summary>
        /// 恢复韧性
        /// </summary>
        void RestoreResistance(float amount);

        /// <summary>
        /// 获取受击位移（用于应用击退效果）
        /// </summary>
        Vector3 GetKnockbackDisplacement();

        /// <summary>
        /// 处理伤害
        /// </summary>
        void HandleDamage(Events.DamageEvent damage);

        /// <summary>
        /// 处理死亡
        /// </summary>
        void HandleDeath();

        /// <summary>
        /// 复活
        /// </summary>
        void HandleResurrect();

        /// <summary>
        /// 解锁层并返回 Base
        /// </summary>
        void UnlockAndReturnToBase();

        /// <summary>
        /// 获取当前层状态描述
        /// </summary>
        string GetActiveStateDescription();

        /// <summary>
        /// 获取当前状态
        /// </summary>
        string GetCurrentState(LayerType layer);
    }
}
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// FSM 配置常量
    /// </summary>
    [System.Serializable]
    public class FSMConfig
    {
        [Header("韧性系统")]
        [Tooltip("最大韧性值")]
        public float MaxResistance = 100f;

        [Header("HitFSM 状态时长")]
        [Tooltip("普通受击僵直时长")]
        public float HitDuration = 0.2f;
        [Tooltip("击退状态时长")]
        public float KnockbackDuration = 0.4f;
        [Tooltip("浮空状态时长")]
        public float LaunchedDuration = 1.0f;
        [Tooltip("眩晕默认时长")]
        public float DizzyDuration = 2.0f;
        [Tooltip("倒地状态时长")]
        public float DownDuration = 2.0f;
        [Tooltip("起身动画时长")]
        public float GetUpDuration = 0.5f;

        [Header("击退物理")]
        [Tooltip("击退减速系数")]
        public float KnockbackDeceleration = 5f;
        [Tooltip("浮空重力加速度")]
        public float LaunchGravity = 20f;
        [Tooltip("浮空水平减速系数")]
        public float LaunchHorizontalDrag = 0.98f;

        /// <summary>
        /// 默认配置
        /// </summary>
        public static FSMConfig Default => new FSMConfig();
    }
}
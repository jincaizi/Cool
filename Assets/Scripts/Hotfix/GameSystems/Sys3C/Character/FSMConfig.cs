using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// FSM configuration constants
    /// </summary>
    [System.Serializable]
    public class FSMConfig
    {
        [Header("韧性系统")]
        // Maximum resistance value
        [Tooltip("最大韧性值")]
        public float MaxResistance = 100f;

        [Header("HitFSM 状态时长")]
        // Normal hit stagger duration
        [Tooltip("普通受击僵直时长")]
        public float HitDuration = 0.2f;
        // Knockback state duration
        [Tooltip("击退状态时长")]
        public float KnockbackDuration = 0.4f;
        // Launched state duration
        [Tooltip("浮空状态时长")]
        public float LaunchedDuration = 1.0f;
        // Default dizzy duration
        [Tooltip("眩晕默认时长")]
        public float DizzyDuration = 2.0f;
        // Down state duration
        [Tooltip("倒地状态时长")]
        public float DownDuration = 2.0f;
        // Get up animation duration
        [Tooltip("起身动画时长")]
        public float GetUpDuration = 0.5f;

        [Header("击退物理")]
        // Knockback deceleration coefficient
        [Tooltip("击退减速系数")]
        public float KnockbackDeceleration = 5f;
        // Launch gravity acceleration
        [Tooltip("浮空重力加速度")]
        public float LaunchGravity = 20f;
        // Launch horizontal drag coefficient
        [Tooltip("浮空水平减速系数")]
        public float LaunchHorizontalDrag = 0.98f;

        /// <summary>
        /// Default configuration
        /// </summary>
        public static FSMConfig Default => new FSMConfig();
    }
}
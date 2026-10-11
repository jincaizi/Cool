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
        // Normal hit stagger duration（0.5s = 实测 Hit 剪辑 0.4667s 之上留 ~1 帧余量）。
        // C1：HitFSM.Update 此前零调用者，这两个值是惰性的、从未被验证；一旦被驱动，
        // 低于剪辑时长就会把受击动画拦腰截断——计时器的职责是**超时兜底**，不是截断器。
        // 契约测试 HitAnimatorContractTests.test_hit_durations_are_at_least_the_hit_clip_length 锁死。
        [Tooltip("普通受击僵直时长（须 ≥ Hit 剪辑时长）")]
        public float HitDuration = 0.5f;
        // Knockback state duration（Knockback 复用同一条 Hit 剪辑，故同样 ≥ 其时长）
        [Tooltip("击退状态时长（须 ≥ Hit 剪辑时长）")]
        public float KnockbackDuration = 0.5f;
        // Launched state duration
        [Tooltip("浮空状态时长")]
        public float LaunchedDuration = 1.0f;
        // Default dizzy duration
        [Tooltip("眩晕默认时长")]
        public float DizzyDuration = 2.0f;
        // Down state duration
        [Tooltip("倒地状态时长")]
        public float DownDuration = 2.0f;
        // Get up animation duration（0.9s = 实测 GetUp 剪辑 0.8334s 之上留 ~2 帧余量）。
        // N4：与上两条同理——计时器是超时兜底、不是截断器。GetUp 状态**没有**挂
        // StateMachineBehaviour（Down 也没有），所以"剪辑播完 → Recover"这条边在生产里
        // 到不了，起身的唯一出口就是这个计时器；原值 0.5s 会把 0.8334s 的起身动画截掉 40%。
        [Tooltip("起身动画时长（须 ≥ GetUp 剪辑时长）")]
        public float GetUpDuration = 0.9f;

        [Header("防御受击")]
        [Tooltip("防御受击动画时长")]
        public float DefendHitDuration = 0.4f;

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
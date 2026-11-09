using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 输入阻断门 —— 多个原因合并（竖屏遮罩、失焦等）。任一原因存在即阻断，
    /// 仅移除最后一个原因才放行。
    ///
    /// 状态是静态的：玩家被销毁重建、切换场景都不能把阻断丢掉；页面方向遮罩在
    /// 角色尚未创建时也必须生效。`InputManager` 订阅变化并立即取消按住，
    /// 不等待下一次采样。
    /// </summary>
    public static class InputBlockGate
    {
        [Flags]
        public enum Reason
        {
            None = 0,
            Landscape = 1 << 0,
            FocusLost = 1 << 1,
        }

        private static Reason _reasons;

        public static bool IsBlocked => _reasons != Reason.None;

        public static Reason ActiveReasons => _reasons;

        /// <summary>阻断状态发生变化时触发（仅发生变化才触发，重复 Set 同一状态不会触发）。</summary>
        public static event Action Changed;

        public static void Set(Reason reason, bool blocked)
        {
            if (reason == Reason.None) return;

            Reason next = blocked ? (_reasons | reason) : (_reasons & ~reason);
            if (next == _reasons) return;

            _reasons = next;
            Changed?.Invoke();
        }

        /// <summary>测试与场景重置用；运行时不要用它代替具体原因的解除。</summary>
        public static void ClearAll()
        {
            if (_reasons == Reason.None) return;

            _reasons = Reason.None;
            Changed?.Invoke();
        }

        /// <summary>
        /// 关闭 Domain Reload 时静态字段会跨 Play 会话残留；进入 PlayMode 时清一次。
        /// 场景加载前执行，早于任何 InputManager 的 OnEnable。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEnter()
        {
            _reasons = Reason.None;
            Changed = null;
        }
    }
}

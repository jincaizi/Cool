using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 移动端动作按钮 —— 按下时置位，由 MobileInputBinder 每帧汇入 InputManager 的
    /// 既有按下语义。不自造第二套输入通道。
    /// </summary>
    public class MobileActionButtons : MonoBehaviour
    {
        public bool AttackHeld { get; private set; }
        public bool DefendHeld { get; private set; }
        public bool JumpPressed { get; private set; }

        // 三个按钮在 Inspector 里各绑 OnPointerDown/Up（不在此写死 UI 结构）
        public void OnAttackDown() => AttackHeld = true;
        public void OnAttackUp() => AttackHeld = false;
        public void OnDefendDown() => DefendHeld = true;
        public void OnDefendUp() => DefendHeld = false;
        public void OnJumpDown() => JumpPressed = true;

        /// <summary>由 MobileInputBinder 在每帧末调用，消费掉一次性按下</summary>
        public void ConsumeJump() => JumpPressed = false;
    }
}

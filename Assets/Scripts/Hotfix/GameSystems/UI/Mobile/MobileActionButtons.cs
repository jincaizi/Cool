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
        public bool TargetSwitchPressed { get; private set; }
        public bool SkillQPressed { get; private set; }
        public bool SkillRPressed { get; private set; }
        public bool FireballPressed { get; private set; }
        public bool FireballHeld { get; private set; }
        public bool ChargeBlastPressed { get; private set; }
        public bool ChargeBlastHeld { get; private set; }
        public bool MeteorPressed { get; private set; }

        // 三个按钮在 Inspector 里各绑 OnPointerDown/Up（不在此写死 UI 结构）
        public void OnAttackDown() => AttackHeld = true;
        public void OnAttackUp() => AttackHeld = false;
        public void OnDefendDown() => DefendHeld = true;
        public void OnDefendUp() => DefendHeld = false;
        public void OnJumpDown() => JumpPressed = true;
        public void OnSkillQDown() => SkillQPressed = true;
        public void OnSkillRDown() => SkillRPressed = true;
        public void OnFireballDown()
        {
            FireballPressed = true;
            FireballHeld = true;
        }
        public void OnFireballUp() => FireballHeld = false;
        public void OnChargeBlastDown()
        {
            ChargeBlastPressed = true;
            ChargeBlastHeld = true;
        }
        public void OnChargeBlastUp() => ChargeBlastHeld = false;
        public void OnMeteorDown() => MeteorPressed = true;

        /// <summary>由"切换目标"按钮的 onClick 调用，置位一次性按下。</summary>
        public void NotifyTargetSwitch() => TargetSwitchPressed = true;

        /// <summary>由 MobileInputBinder 在每帧末调用，消费掉一次性按下</summary>
        public void ConsumeJump() => JumpPressed = false;

        /// <summary>由 MobileInputBinder 在每帧末调用，消费掉一次性按下</summary>
        public void ConsumeTargetSwitch() => TargetSwitchPressed = false;

        public void ConsumeSkillPresses()
        {
            SkillQPressed = false;
            SkillRPressed = false;
            FireballPressed = false;
            ChargeBlastPressed = false;
            MeteorPressed = false;
        }

        public void ClearCombatInput()
        {
            AttackHeld = false;
            DefendHeld = false;
            JumpPressed = false;
            TargetSwitchPressed = false;
            FireballHeld = false;
            ChargeBlastHeld = false;
            ConsumeSkillPresses();
        }

        /// <summary>
        /// 停用/失焦时清空 —— 与摇杆/观察区同一类缺陷：抬指事件因浏览器抢焦点丢失时
        /// `AttackHeld` 会锁存为 true，而 binder 每帧把它汇入 `SetAttackHeld(true)`
        /// ⇒ **自动连续攻击**（手机上才会现形）。
        /// </summary>
        private void OnDisable()
        {
            ClearCombatInput();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ClearCombatInput();
        }
    }
}

using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class KeyboardInputAdapterTests
    {
        [Test]
        public void test_keyboard_look_is_zero_without_right_button()
        {
            // Arrange & Act
            Vector2 look = KeyboardInputAdapter.GateLookToRightDrag(
                new Vector2(3f, 4f), rightButtonHeld: false, touchActive: false);

            // Assert
            Assert.AreEqual(Vector2.zero, look, "未按右键时鼠标移动不得转视角");
        }

        [Test]
        public void test_keyboard_look_passes_through_with_right_button()
        {
            // Arrange & Act
            Vector2 look = KeyboardInputAdapter.GateLookToRightDrag(
                new Vector2(3f, 4f), rightButtonHeld: true, touchActive: false);

            // Assert
            Assert.AreEqual(new Vector2(3f, 4f), look);
        }

        [Test]
        public void test_keyboard_look_is_zero_while_touch_active()
        {
            // Arrange & Act
            Vector2 look = KeyboardInputAdapter.GateLookToRightDrag(
                new Vector2(3f, 4f), rightButtonHeld: true, touchActive: true);

            // Assert
            Assert.AreEqual(Vector2.zero, look, "触摸活跃时合成鼠标输入不得转视角");
        }
    }
}

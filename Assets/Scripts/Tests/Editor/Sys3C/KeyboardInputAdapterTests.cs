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
                new Vector2(3f, 4f), rightButtonHeld: false);

            // Assert
            Assert.AreEqual(Vector2.zero, look, "未按右键时鼠标移动不得转视角");
        }

        [Test]
        public void test_keyboard_look_passes_through_with_right_button()
        {
            // Arrange & Act
            Vector2 look = KeyboardInputAdapter.GateLookToRightDrag(
                new Vector2(3f, 4f), rightButtonHeld: true);

            // Assert
            Assert.AreEqual(new Vector2(3f, 4f), look);
        }
    }
}

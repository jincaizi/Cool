using System;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 输入阻断门：多原因合并，只有最后一个原因解除才放行。
    /// 静态状态必须由测试自行清理，否则会泄漏到同域内的其它输入测试。
    /// </summary>
    public class InputBlockGateTests
    {
        [SetUp]
        public void SetUp()
        {
            InputBlockGate.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            InputBlockGate.ClearAll();
        }

        [Test]
        public void test_gate_landscape_and_focus_loss_merge_until_both_clear()
        {
            // Arrange
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
            InputBlockGate.Set(InputBlockGate.Reason.FocusLost, true);

            // Act & Assert
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, false);
            Assert.IsTrue(InputBlockGate.IsBlocked, "仍存在失焦阻断时必须保持阻断");

            InputBlockGate.Set(InputBlockGate.Reason.FocusLost, false);
            Assert.IsFalse(InputBlockGate.IsBlocked, "最后一个原因解除后才放行");
        }

        [Test]
        public void test_gate_repeated_set_does_not_raise_changed_twice()
        {
            // Arrange
            int changes = 0;
            Action handler = () => changes++;
            InputBlockGate.Changed += handler;

            try
            {
                // Act
                InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
                InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
                InputBlockGate.Set(InputBlockGate.Reason.Landscape, false);
                InputBlockGate.Set(InputBlockGate.Reason.Landscape, false);

                // Assert：只关心状态真正变化的两次
                Assert.AreEqual(2, changes);
            }
            finally
            {
                InputBlockGate.Changed -= handler;
            }
        }

        [Test]
        public void test_gate_clear_all_releases_every_reason()
        {
            // Arrange
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
            InputBlockGate.Set(InputBlockGate.Reason.FocusLost, true);

            // Act
            InputBlockGate.ClearAll();

            // Assert
            Assert.IsFalse(InputBlockGate.IsBlocked);
            Assert.AreEqual(InputBlockGate.Reason.None, InputBlockGate.ActiveReasons);
        }
    }
}

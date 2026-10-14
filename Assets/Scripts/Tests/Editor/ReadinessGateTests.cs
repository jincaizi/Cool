using Cysharp.Threading.Tasks;
using Hotfix.GameSystems.UI;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 2：就绪闸门的语义。它是 `while (…) await UniTask.Delay(50)` 轮询的替代品 ——
    /// 靠"等待与放行解耦"消除顺序依赖，而不是靠反复试探。
    /// 用 `UniTask.Status` 断言，所以不需要 await（EditMode 测试跑不了协程）。
    /// </summary>
    public class ReadinessGateTests
    {
        [Test]
        public void test_gate_pends_until_released()
        {
            // Arrange
            var gate = new ReadinessGate();

            // Assert
            Assert.AreEqual(UniTaskStatus.Pending, gate.WaitAsync().Status,
                "未放行前必须是 Pending —— 这正是'先到者等后来者'的那一半");
            Assert.IsFalse(gate.IsReleased);
        }

        [Test]
        public void test_gate_completes_for_waiters_registered_before_and_after_release()
        {
            // Arrange
            var gate = new ReadinessGate();
            var before = gate.WaitAsync();   // 放行之前就登记

            // Act
            gate.Release();

            // Assert
            Assert.AreEqual(UniTaskStatus.Succeeded, before.Status, "放行前登记的等待者必须被放行");
            Assert.AreEqual(UniTaskStatus.Succeeded, gate.WaitAsync().Status,
                "放行之后才来的等待者必须立刻通过 —— 这是解耦的另一半");
            Assert.IsTrue(gate.IsReleased);
        }

        [Test]
        public void test_release_is_idempotent()
        {
            var gate = new ReadinessGate();

            gate.Release();
            gate.Release();
            gate.Release();

            Assert.AreEqual(UniTaskStatus.Succeeded, gate.WaitAsync().Status);
            Assert.IsTrue(gate.IsReleased);
        }
    }
}

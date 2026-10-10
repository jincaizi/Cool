using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// Task 6：摇杆适配器的模拟量语义 —— 这是 P6 审查记录过的坑：
    /// 推一半必须走一半，不能被 normalized 抹成全速。
    /// </summary>
    public class MobileInputAdapterTests
    {
        [Test]
        public void test_half_push_keeps_half_magnitude()
        {
            // Arrange（摇杆推到一半）
            var adapter = new JoystickInputAdapter(() => new Vector2(0.5f, 0f), () => Vector2.zero);

            // Act
            Vector3 move = adapter.GetMoveInput();

            // Assert（关键：不 normalize —— 原实现的 normalized 会让推一半也全速）
            Assert.AreEqual(0.5f, Mathf.Abs(move.x), 0.001f, "推一半必须走一半");
            Assert.AreEqual(0f, move.z, 0.001f);
        }

        [Test]
        public void test_dead_zone_returns_zero()
        {
            // Arrange（推到死区内）
            var adapter = new JoystickInputAdapter(() => new Vector2(0.02f, 0f), () => Vector2.zero);

            // Act & Assert
            Assert.AreEqual(Vector3.zero, adapter.GetMoveInput());
            Assert.IsFalse(adapter.HasMoveInput());
        }

        [Test]
        public void test_full_push_reaches_full_magnitude()
        {
            // Arrange
            var adapter = new JoystickInputAdapter(() => new Vector2(0f, 1f), () => Vector2.zero);

            // Act
            Vector3 move = adapter.GetMoveInput();

            // Assert（满推仍是 1，未被放大或钳制错）
            Assert.AreEqual(1f, move.z, 0.001f);
        }

        [Test]
        public void test_look_area_delta_is_forwarded()
        {
            // Arrange
            var adapter = new JoystickInputAdapter(() => Vector2.zero, () => new Vector2(3f, -2f));

            // Act
            Vector2 look = adapter.GetCameraRotationInput();

            // Assert（触屏拖拽量原样透传，灵敏度由相机侧既有参数负责）
            Assert.AreEqual(new Vector2(3f, -2f), look);
            Assert.IsTrue(adapter.HasCameraRotationInput());
        }
    }
}

using System;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 3a 入口项：把阶段 2 那句"声明与预制体不符要报错"从**纯函数**钉到**接线**上。
    ///
    /// 为什么必须补：`DescribeModeMismatch` 单独测过了，但 `Register` 里那 10 行调用它的话
    /// **整段删掉也不会有任何测试报红**。而阶段 4 会重写 `Register` —— 不钉住，
    /// 阶段 2 的修复会被无声抹掉。
    ///
    /// EditMode 下 `AddComponent` 不触发 `Awake`，所以 `UIManager` 是"半成品"、
    /// `GetPanel` 之类的运行时路径走不通 —— 这正好够用：本测试只验注册期那一段。
    /// </summary>
    public class UIManagerRegistrationTests
    {
        /// <summary>声明 CanvasSwitch 但身上没有 Canvas —— 就是那个"永远关不掉"的接线错误。</summary>
        private class CanvasSwitchPanel : UIPanel
        {
            public override LayerType Layer => LayerType.Base;
            public override VisibilityMode Mode => VisibilityMode.CanvasSwitch;
        }

        /// <summary>形状正常的面板。</summary>
        private class HealthyPanel : UIPanel
        {
            public override LayerType Layer => LayerType.Base;
            public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        }

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_register_throws_when_the_declared_mode_has_no_matching_component()
        {
            // Arrange：面板声明 CanvasSwitch，但 GameObject 上没有 Canvas
            _go = new GameObject("BadPanel");
            var panel = _go.AddComponent<CanvasSwitchPanel>();
            var manager = _go.AddComponent<UIManager>();

            // Act + Assert
            Assert.Throws<InvalidOperationException>(
                () => manager.Register(typeof(CanvasSwitchPanel), panel),
                "声明 CanvasSwitch 而没有 Canvas 的面板永远关不掉 —— Register 必须拦下它");
        }

        [Test]
        public void test_a_rejected_panel_is_not_registered()
        {
            // Arrange
            _go = new GameObject("BadPanel");
            var panel = _go.AddComponent<CanvasSwitchPanel>();
            var manager = _go.AddComponent<UIManager>();

            // Act
            try { manager.Register(typeof(CanvasSwitchPanel), panel); }
            catch (InvalidOperationException) { /* 预期 */ }

            // Assert：校验必须发生在写入字典**之前** —— 否则就是一个"注册了但不可用"的面板
            Assert.IsNull(manager.GetPanel<CanvasSwitchPanel>(),
                "被校验拒绝的面板不得留在注册表里");
        }

        [Test]
        public void test_register_accepts_a_healthy_panel()
        {
            // Arrange
            _go = new GameObject("GoodPanel");
            var panel = _go.AddComponent<HealthyPanel>();
            var manager = _go.AddComponent<UIManager>();

            // Act
            manager.Register(typeof(HealthyPanel), panel);

            // Assert
            Assert.AreSame(panel, manager.GetPanel<HealthyPanel>());
        }

        [Test]
        public void test_register_guarantees_a_canvas_group_on_the_panel_root()
        {
            // Arrange：HealthyPanel 起手没有 CanvasGroup
            _go = new GameObject("GoodPanel");
            var panel = _go.AddComponent<HealthyPanel>();
            var manager = _go.AddComponent<UIManager>();
            Assert.IsNull(_go.GetComponent<CanvasGroup>(), "前置：起手不该有 CanvasGroup");

            // Act
            manager.Register(typeof(HealthyPanel), panel);

            // Assert：阶段 3 的事件层要用 CanvasGroup.interactable
            Assert.IsNotNull(_go.GetComponent<CanvasGroup>(), "注册后面板根必须有 CanvasGroup");
        }
    }
}

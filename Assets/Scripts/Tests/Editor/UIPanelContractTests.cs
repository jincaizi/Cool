using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 2：面板声明的显隐模式必须与预制体实际组件相符。
    ///
    /// 修的是"静默失效"：`ApplyVisibilityOff/On` 每个分支都判空，于是声明与预制体不符时
    /// 表现为**什么都不做**——选 CanvasSwitch 而没有 Canvas，面板永远关不掉且零日志。
    /// 实测三个面板预制体没有一个带 Canvas 组件，第 4 个面板中招只是时间问题。
    /// </summary>
    public class UIPanelContractTests
    {
        /// <summary>探针面板：形状取最普通的一种，只为拿到一个具体子类。</summary>
        private class ProbePanel : UIPanel
        {
            public override LayerType Layer => LayerType.Base;
            public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        }

        [Test]
        public void test_canvas_switch_without_a_canvas_component_is_reported()
        {
            // Act
            var reason = UIPanel.DescribeModeMismatch(
                VisibilityMode.CanvasSwitch, hasCanvas: false, hasCanvasGroup: true);

            // Assert
            Assert.IsNotNull(reason, "选 CanvasSwitch 而预制体没有 Canvas，就是那个'关不掉'的场景，必须报出来");
            StringAssert.Contains("Canvas", reason);
        }

        [Test]
        public void test_matching_declarations_report_no_mismatch()
        {
            Assert.IsNull(UIPanel.DescribeModeMismatch(VisibilityMode.CanvasSwitch, hasCanvas: true,  hasCanvasGroup: false),
                "有 Canvas 的 CanvasSwitch 是合法的");
            Assert.IsNull(UIPanel.DescribeModeMismatch(VisibilityMode.CanvasGroup,  hasCanvas: false, hasCanvasGroup: true),
                "有 CanvasGroup 的 CanvasGroup 是合法的");
            Assert.IsNull(UIPanel.DescribeModeMismatch(VisibilityMode.CanvasGroup,  hasCanvas: false, hasCanvasGroup: false),
                "缺 CanvasGroup 不算不匹配——Register 会补齐（见 EnsureCanvasGroup）");
            Assert.IsNull(UIPanel.DescribeModeMismatch(VisibilityMode.ToggleActive, hasCanvas: false, hasCanvasGroup: false),
                "ToggleActive 不需要额外组件");
        }

        [Test]
        public void test_ensure_canvas_group_adds_exactly_one_and_is_idempotent()
        {
            // Arrange
            var go = new GameObject("EnsureCanvasGroupProbe");
            try
            {
                var panel = go.AddComponent<ProbePanel>();
                Assert.IsNull(go.GetComponent<CanvasGroup>(), "前置：起手不该有 CanvasGroup");

                // Act
                var first = panel.EnsureCanvasGroup();
                var second = panel.EnsureCanvasGroup();

                // Assert
                Assert.IsNotNull(first);
                Assert.AreSame(first, second, "重复调用必须复用同一个，不能越加越多");
                Assert.AreEqual(1, go.GetComponents<CanvasGroup>().Length);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

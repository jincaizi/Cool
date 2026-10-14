using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 1：动画子系统（UIAnimation/UIAnimPreset/SequenceBuilder/UITweenExtensions）被删除后，
    /// UIPanel 的两个动画钩子必须**保留**并默认不播任何动画 —— 它们是 2026-05-08 设计文档 §6.2
    /// 记录的生命周期契约，阶段 5 的动画工作接在这里。
    /// 本测试是表征测试：删除前后都必须通过。
    /// </summary>
    public class UIPanelLifecycleTests
    {
        private class NoAnimPanel : UIPanel
        {
            public override LayerType Layer => LayerType.Base;
            public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        }

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_panel_animation_hooks_default_to_no_animation()
        {
            // Arrange
            _go = new GameObject("NoAnimPanel");
            var panel = _go.AddComponent<NoAnimPanel>();

            // Assert：尚未接动画的面板必须返回 null，UIManager 据此跳过等待
            Assert.IsNull(panel.PlayShowAnimation(), "默认入场钩子必须返回 null");
            Assert.IsNull(panel.PlayHideAnimation(), "默认出场钩子必须返回 null");
        }
    }
}

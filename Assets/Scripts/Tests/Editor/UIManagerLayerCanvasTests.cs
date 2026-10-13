using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 1：Canvas 的创建从 UIManager.Awake 的私有循环里抽出来后，第一次可以被直接断言。
    /// 断言的是**层表到 Canvas 的契约**：名字、排序号、组件集 —— 这些都是阶段 3 重排层号时
    /// 会碰到的东西，先钉死当前值。
    /// </summary>
    public class UIManagerLayerCanvasTests
    {
        private GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("LayerHost");
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null) Object.DestroyImmediate(_parent);
        }

        [Test]
        public void test_every_layer_builds_a_named_canvas_with_the_layer_sort_order()
        {
            foreach (var (layer, sortOrder) in UIConst.Layers)
            {
                // Act
                var canvas = UIManager.BuildLayerCanvas(_parent.transform, layer, sortOrder);

                // Assert
                Assert.IsNotNull(canvas, layer + " 未生成 Canvas");
                Assert.AreEqual("Canvas_Layer_" + layer, canvas.gameObject.name,
                    "Canvas 命名必须保持 Canvas_Layer_<层名>（阶段 1 零行为变化）");
                Assert.AreEqual(sortOrder, canvas.sortingOrder, layer + " 的排序号与层表不符");
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode,
                    layer + " 必须是 ScreenSpaceOverlay");
                Assert.AreEqual(_parent.transform, canvas.transform.parent, "Canvas 未挂到父节点下");
            }
        }

        [Test]
        public void test_every_layer_canvas_carries_scaler_raycaster_and_screen_adapter()
        {
            foreach (var (layer, sortOrder) in UIConst.Layers)
            {
                // Act
                var canvas = UIManager.BuildLayerCanvas(_parent.transform, layer, sortOrder);

                // Assert
                Assert.IsNotNull(canvas.GetComponent<CanvasScaler>(),
                    layer + " 缺 CanvasScaler —— 分辨率适配会失效");
                Assert.IsNotNull(canvas.GetComponent<GraphicRaycaster>(),
                    layer + " 缺 GraphicRaycaster —— 该层将无法接收点击");
                Assert.IsNotNull(canvas.GetComponent<ScreenAdapter>(),
                    layer + " 缺 ScreenAdapter —— 安全区与缩放配置不会生效");
            }
        }
    }
}

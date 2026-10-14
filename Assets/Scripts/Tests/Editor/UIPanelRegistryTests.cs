using System.Linq;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 2：`UIManager.PanelRegistry` 是全项目唯一的面板清单。
    ///
    /// 这一条替代了规格 §6 说的"编辑器校验器"：漏注册的面板**在 EditMode 里当场报红**，
    /// 而不是等到运行时某个 `GetPanel&lt;T&gt;()` 静默返回 null。
    /// </summary>
    public class UIPanelRegistryTests
    {
        [Test]
        public void test_every_concrete_panel_type_is_in_the_registry()
        {
            // Arrange
            var registered = UIManager.PanelRegistry.Select(e => e.panelType).ToArray();
            var panelTypes = typeof(UIPanel).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(UIPanel)) && !t.IsAbstract)
                .ToArray();

            // Assert
            Assert.IsNotEmpty(panelTypes, "反射一个 UIPanel 子类都没找到，测试本身有问题");
            foreach (var type in panelTypes)
            {
                CollectionAssert.Contains(registered, type,
                    type.Name + " 是 UIPanel 子类但不在 UIManager.PanelRegistry 里 —— 漏注册的面板永远打不开");
            }
        }

        [Test]
        public void test_registry_entries_are_well_formed()
        {
            var types = UIManager.PanelRegistry.Select(e => e.panelType).ToArray();

            Assert.AreEqual(types.Length, types.Distinct().Count(), "注册表里有重复的类型");

            foreach (var (panelType, prefabPath) in UIManager.PanelRegistry)
            {
                Assert.IsTrue(panelType.IsSubclassOf(typeof(UIPanel)),
                    panelType.Name + " 不是 UIPanel 子类");
                Assert.IsFalse(string.IsNullOrWhiteSpace(prefabPath),
                    panelType.Name + " 的预制体路径为空");
            }
        }

        [Test]
        public void test_every_registry_prefab_path_resolves()
        {
            // Resources.Load 在 EditMode 下可用 —— 于是路径打错在测试期就红，
            // 而不是等到运行时某次 Awake 里打一条 LogError
            foreach (var (panelType, prefabPath) in UIManager.PanelRegistry)
            {
                var prefab = Resources.Load<GameObject>(prefabPath);
                Assert.IsNotNull(prefab,
                    "Resources/" + prefabPath + "（注册表里 " + panelType.Name + " 的预制体）加载不到");
                Assert.IsNotNull(prefab.GetComponent<UIPanel>(),
                    prefabPath + " 上没有 UIPanel 组件");
                Assert.IsTrue(panelType.IsInstanceOfType(prefab.GetComponent<UIPanel>()),
                    prefabPath + " 上的组件不是注册表声明的 " + panelType.Name);
            }
        }
    }
}

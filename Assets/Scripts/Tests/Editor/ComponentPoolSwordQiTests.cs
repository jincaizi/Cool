using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class ComponentPoolSwordQiTests
    {
        // 显式跟踪创建的 GameObject（含 prefab 容器），TearDown 确定性清理
        private readonly List<GameObject> _gos = new List<GameObject>();

        [Test]
        public void test_component_pool_get_recreates_destroyed_instance()
        {
            // Arrange
            var prefabGo = NewGo("QiPrefab");
            var prefabComp = prefabGo.AddComponent<SwordQiProjectile>();
            var pool = new ComponentPool<SwordQiProjectile>(prefabComp);
            var first = pool.Get();
            pool.Return(first);
            Object.DestroyImmediate(first.gameObject); // 模拟场景销毁后池内残留

            // Act
            var second = pool.Get();

            // Assert
            Assert.IsNotNull(second, "池内已销毁实例应被重建");
            Assert.IsTrue(second.gameObject.activeSelf);
        }

        [Test]
        public void test_component_pool_return_reuses_same_instance()
        {
            // Arrange
            var prefabGo = NewGo("QiPrefab");
            var prefabComp = prefabGo.AddComponent<SwordQiProjectile>();
            var pool = new ComponentPool<SwordQiProjectile>(prefabComp);

            // Act
            var first = pool.Get();
            pool.Return(first);
            var second = pool.Get();

            // Assert
            Assert.AreSame(first, second, "回池后再次 Get 应复用同一实例");
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            // 清理测试创建的 GameObject（Unity == null 重载可安全处理已销毁实例）
            foreach (var go in _gos)
                if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();

            // 兜底清理池内残余实例（含可能因场景销毁残留的伪实例）
            foreach (var comp in Object.FindObjectsOfType<SwordQiProjectile>())
            {
                if (comp != null && comp.gameObject != null)
                    Object.DestroyImmediate(comp.gameObject);
            }
        }
    }
}

using System.Collections;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 阶段 2：bootstrap 的幂等性。它要在"场景里什么都没有"的前提下把
    /// UIManager 与 EventSystem 各建一份，且重复调用不能越建越多。
    ///
    /// 必须是 PlayMode：EditMode 下 AddComponent 不会触发 Awake，
    /// `UIManager.Instance` 永远是 null，幂等性根本测不出来。
    /// </summary>
    public class UIBootstrapTests
    {
        [UnityTest]
        public IEnumerator test_ensure_ui_manager_is_idempotent()
        {
            var first = UIBootstrap.EnsureUIManager();
            yield return null;

            Assert.IsNotNull(first, "第一次调用必须建出 UIManager");
            Assert.AreSame(first, UIManager.Instance, "Instance 必须指向它");

            var second = UIBootstrap.EnsureUIManager();
            Assert.AreSame(first, second, "重复调用必须复用同一个");

            var count = Object.FindObjectsOfType<UIManager>(includeInactive: true).Length;
            Assert.AreEqual(1, count, "场景里只能有一个 UIManager，实际 " + count);
        }

        [UnityTest]
        public IEnumerator test_ensure_event_system_is_idempotent()
        {
            var first = UIBootstrap.EnsureEventSystem();
            yield return null;

            Assert.IsNotNull(first);

            var second = UIBootstrap.EnsureEventSystem();
            Assert.AreSame(first, second, "重复调用必须复用同一个");

            Assert.IsNotNull(first.GetComponent<StandaloneInputModule>(),
                "EventSystem 必须带输入模块，否则 UI 收不到点击");
        }
    }
}

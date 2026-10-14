using UnityEngine;
using UnityEngine.EventSystems;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 启动期统一创建 UIManager 与 EventSystem（规格 R8）。
    ///
    /// 为什么必须做：此前**两个场景各带一份** UIManager 与 EventSystem，跨场景时靠
    /// "先加载的场景先 Awake"裁决单例 —— 隐式顺序依赖，且场景里漏放一份就没有 UI。
    /// 改为一处创建、`DontDestroyOnLoad`，场景里不再放。
    ///
    /// 用 `AfterSceneLoad` 而不是 `BeforeSceneLoad`：此时场景里的对象都已 Awake，
    /// 于是 `Instance` / `EventSystem.current` 的判空能真正起作用 —— 既容忍场景里还留着
    /// 一份（过渡期安全），也容忍测试场景自带 EventSystem（不会建出第二个）。
    ///
    /// 形制follow `MobileQualityBootstrap`：RuntimeInitializeOnLoadMethod 薄壳 + 可单测的 Ensure* 内核。
    /// </summary>
    public static class UIBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Ensure()
        {
            EnsureUIManager();
            EnsureEventSystem();
        }

        /// <summary>返回当前 UIManager，没有就建一个。幂等。</summary>
        public static UIManager EnsureUIManager()
        {
            if (UIManager.Instance != null)
                return UIManager.Instance;

            var go = new GameObject("UIManager");
            return go.AddComponent<UIManager>();   // Awake 里会 DontDestroyOnLoad
        }

        /// <summary>返回当前 EventSystem，没有就建一个。幂等。</summary>
        public static EventSystem EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return EventSystem.current;

            var go = new GameObject("EventSystem");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            return go.GetComponent<EventSystem>();
        }
    }
}

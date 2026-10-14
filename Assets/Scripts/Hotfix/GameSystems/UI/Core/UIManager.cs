using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        private readonly Canvas[] _layers = new Canvas[UIConst.Layers.Length];

        /// <summary>
        /// 全项目唯一的面板清单。**加一个面板 = 这里加一行 + 加一个预制体。**
        /// 漏注册由 UIPanelRegistryTests 在 EditMode 里当场报红（它反射全部 UIPanel 子类）。
        ///
        /// 路由用**类型**而不是字符串：改名、打错、复制粘贴都会变成编译错误，
        /// 不再有 `GetPanel&lt;T&gt;() == null` 这种静默失败。
        /// </summary>
        internal static readonly (System.Type panelType, string prefabPath)[] PanelRegistry =
        {
            (typeof(PlayerHudPanel),       "Prefabs/UI/PlayerHudPanel"),
            (typeof(TargetPanel),          "Prefabs/UI/TargetPanel"),
            (typeof(CharacterSelectPanel), "Prefabs/UI/CharacterSelectPanel"),
        };

        private readonly Dictionary<System.Type, UIPanel> _panels = new Dictionary<System.Type, UIPanel>();

        private readonly ReadinessGate _ready = new ReadinessGate();

        /// <summary>全部面板加载成功。失败时为 false —— 具体缺哪块用 GetPanel&lt;T&gt;() == null 判断。</summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// 初始化完成的信号。**必然完成** —— InitializeAsync 的 finally 一定会放行，
        /// 所以调用方永远不会永久挂起。这正是它取代轮询的地方：
        /// `while (…) await UniTask.Delay(50)` 在加载失败时是无限空转，没有任何出口。
        /// </summary>
        public UniTask WhenReady() => _ready.WaitAsync();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CreateCanvasLayers();
            InitializeAsync().Forget();
        }

        /// <summary>
        /// 显式初始化。不再是 `async void Awake` —— 那样异常无人接管，
        /// 而且"Awake 什么时候算完"对外没有出口。
        /// `finally` 里无条件放行，是"加载失败也不会挂死"的结构性保证。
        /// </summary>
        private async UniTask InitializeAsync()
        {
            try
            {
                await LoadBuiltInPanels();
                IsReady = true;
            }
            catch (Exception e)
            {
                Debug.LogError("UIManager: 初始化中断 —— " + e.Message);
            }
            finally
            {
                _ready.Release();
            }
        }

        private async UniTask LoadBuiltInPanels()
        {
            foreach (var (panelType, prefabPath) in PanelRegistry)
            {
                await RegisterFromPrefab(panelType, prefabPath);
            }

            if (_panels.TryGetValue(typeof(PlayerHudPanel), out var hud))
            {
                await ShowPanelAsync(hud);
            }

            // 启动流程：先选人，再进世界（选人面板全屏覆盖 HUD）
            await OpenAsync<CharacterSelectPanel>();
        }

        private async UniTask RegisterFromPrefab(System.Type panelType, string resourcePath)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogError($"UIManager: Prefab not found at Resources/{resourcePath}");
                return;
            }

            var instance = Instantiate(prefab);
            var panel = instance.GetComponent<UIPanel>();
            if (panel == null)
            {
                Debug.LogError($"UIManager: Prefab at {resourcePath} has no UIPanel component");
                Destroy(instance);
                return;
            }

            // 注册表说这个路径是 A，预制体上却是 B —— 这是接线错误，不是警告
            if (!panelType.IsInstanceOfType(panel))
            {
                Debug.LogError($"UIManager: 注册表把 {resourcePath} 标成 {panelType.Name}，" +
                               $"但预制体上的组件是 {panel.GetType().Name}");
                Destroy(instance);
                return;
            }

            Register(panelType, panel);
        }

        private void CreateCanvasLayers()
        {
            for (int i = 0; i < UIConst.Layers.Length; i++)
            {
                var (layer, sortOrder) = UIConst.Layers[i];
                _layers[i] = BuildLayerCanvas(transform, layer, sortOrder);
            }
        }

        /// <summary>
        /// 建一层 Canvas。抽成 static 是为了能在 EditMode 里直接断言层表契约 ——
        /// 走 UIManager.Awake 会连带触发 Resources 加载与面板注册，测不动。
        /// </summary>
        internal static Canvas BuildLayerCanvas(Transform parent, LayerType layer, int sortOrder)
        {
            var go = new GameObject("Canvas_Layer_" + layer);
            go.transform.SetParent(parent);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            go.AddComponent<UnityEngine.UI.CanvasScaler>();
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            go.AddComponent<ScreenAdapter>();

            return canvas;
        }

        private void Register(System.Type panelType, UIPanel panel)
        {
            if (panel == null || panelType == null)
            {
                Debug.LogError("UIManager: Cannot register null panel");
                return;
            }

            var mismatch = UIPanel.DescribeModeMismatch(
                panel.Mode,
                hasCanvas: panel.GetComponent<Canvas>() != null,
                hasCanvasGroup: panel.GetComponent<CanvasGroup>() != null);

            if (mismatch != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new System.InvalidOperationException(
                    $"UIManager: 面板 '{panelType.Name}' {mismatch}");
#else
                Debug.LogError($"UIManager: 面板 '{panelType.Name}' {mismatch} —— 已跳过注册");
                return;
#endif
            }

            if (_panels.ContainsKey(panelType))
            {
                Debug.LogWarning($"UIManager: Panel '{panelType.Name}' already registered, replacing.");
            }

            _panels[panelType] = panel;

            // Reparent to correct layer canvas
            var layerIndex = (int)panel.Layer;
            if (layerIndex >= 0 && layerIndex < _layers.Length && _layers[layerIndex] != null)
            {
                panel.transform.SetParent(_layers[layerIndex].transform, false);
            }

            // 阶段 3 的事件层要用 CanvasGroup.interactable，这里先保证它存在
            panel.EnsureCanvasGroup();

            // Start hidden
            panel.ApplyVisibilityOff();
        }

        // ===== Open / Close =====

        public async UniTask OpenAsync<T>() where T : UIPanel
        {
            if (!_panels.TryGetValue(typeof(T), out var panel))
            {
                Debug.LogError($"UIManager: Panel '{typeof(T).Name}' not registered");
                return;
            }

            await ShowPanelAsync(panel);
        }

        public async UniTask CloseAsync<T>() where T : UIPanel
        {
            if (!_panels.TryGetValue(typeof(T), out var panel))
                return;

            await HidePanelAsync(panel);
        }

        // ===== Always (Base) =====

        public async UniTask ShowAlwaysAsync<T>() where T : UIPanel
        {
            if (!_panels.TryGetValue(typeof(T), out var panel))
            {
                Debug.LogError($"UIManager: Panel '{typeof(T).Name}' not registered");
                return;
            }

            await ShowPanelAsync(panel);
        }

        public async UniTask HideAlwaysAsync<T>() where T : UIPanel
        {
            if (!_panels.TryGetValue(typeof(T), out var panel))
                return;

            await HidePanelAsync(panel);
        }

        // ===== Lookup =====

        public T GetPanel<T>() where T : UIPanel
        {
            _panels.TryGetValue(typeof(T), out var panel);
            return panel as T;
        }

        // ===== Internal =====

        private async UniTask ShowPanelAsync(UIPanel panel)
        {
            panel.ApplyVisibilityOn();
            panel.OnPreShow();

            var seq = panel.PlayShowAnimation();
            if (seq != null)
                await WaitForSequence(seq);

            panel.OnShow();
            panel.SetVisible(true);
        }

        private async UniTask HidePanelAsync(UIPanel panel)
        {
            panel.OnHide();

            var seq = panel.PlayHideAnimation();
            if (seq != null)
                await WaitForSequence(seq);

            panel.ApplyVisibilityOff();
            panel.SetVisible(false);
        }

        private static async UniTask WaitForSequence(DG.Tweening.Sequence seq)
        {
            if (seq == null) return;
            while (seq.IsPlaying())
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }
    }
}

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
        private readonly Dictionary<string, UIPanel> _registry = new Dictionary<string, UIPanel>();

        private async void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CreateCanvasLayers();
            await LoadBuiltInPanels();
        }

        private async UniTask LoadBuiltInPanels()
        {
            await RegisterFromPrefab("Prefabs/UI/PlayerHudPanel", "PlayerHudPanel");
            await RegisterFromPrefab("Prefabs/UI/TargetPanel", "TargetPanel");
            await RegisterFromPrefab("Prefabs/UI/CharacterSelectPanel", "CharacterSelectPanel");

            if (_registry.TryGetValue("PlayerHudPanel", out var hud))
            {
                await ShowPanelAsync(hud);
            }

            // 启动流程：先选人，再进世界（选人面板全屏覆盖 HUD）
            if (_registry.TryGetValue("CharacterSelectPanel", out var select))
            {
                await OpenAsync("CharacterSelectPanel");
            }
        }

        private async UniTask RegisterFromPrefab(string resourcePath, string panelId)
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

            if (panel.PanelId != panelId)
            {
                Debug.LogWarning($"UIManager: PanelId mismatch, expected '{panelId}' got '{panel.PanelId}'");
            }

            Register(panel);
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

        public void Register(UIPanel panel)
        {
            if (panel == null || string.IsNullOrEmpty(panel.PanelId))
            {
                Debug.LogError("UIManager: Cannot register null panel or empty PanelId");
                return;
            }

            if (_registry.ContainsKey(panel.PanelId))
            {
                Debug.LogWarning($"UIManager: Panel '{panel.PanelId}' already registered, replacing.");
            }

            var mismatch = UIPanel.DescribeModeMismatch(
                panel.Mode,
                hasCanvas: panel.GetComponent<Canvas>() != null,
                hasCanvasGroup: panel.GetComponent<CanvasGroup>() != null);

            if (mismatch != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new System.InvalidOperationException(
                    $"UIManager: 面板 '{panel.GetType().Name}' {mismatch}");
#else
                Debug.LogError($"UIManager: 面板 '{panel.GetType().Name}' {mismatch} —— 已跳过注册");
                return;
#endif
            }

            _registry[panel.PanelId] = panel;

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

        // ===== Overlay (Top / Guide) =====

        public async UniTask OpenAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
            {
                Debug.LogError($"UIManager: Panel '{panelId}' not registered");
                return;
            }

            await ShowPanelAsync(panel);
        }

        public async UniTask CloseAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
                return;

            await HidePanelAsync(panel);
        }

        // ===== Always (Base) =====

        public async UniTask ShowAlwaysAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
            {
                Debug.LogError($"UIManager: Panel '{panelId}' not registered");
                return;
            }

            await ShowPanelAsync(panel);
        }

        public async UniTask HideAlwaysAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
                return;

            await HidePanelAsync(panel);
        }

        // ===== Lookup =====

        public T GetPanel<T>(string panelId) where T : UIPanel
        {
            _registry.TryGetValue(panelId, out var panel);
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

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

        private readonly Canvas[] _layers = new Canvas[5];
        private readonly Dictionary<string, UIPanel> _registry = new Dictionary<string, UIPanel>();
        private readonly Stack<UIPanel> _stack = new Stack<UIPanel>();
        private readonly HashSet<UIPanel> _activeOverlays = new HashSet<UIPanel>();

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
        }

        private void CreateCanvasLayers()
        {
            for (int i = 0; i < 5; i++)
            {
                var go = new GameObject($"Canvas_Layer_{(LayerType)i}");
                go.transform.SetParent(transform);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = UIConst.SortOrders[i];

                go.AddComponent<UnityEngine.UI.CanvasScaler>();
                go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                go.AddComponent<ScreenAdapter>();

                _layers[i] = canvas;
            }
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

            _registry[panel.PanelId] = panel;

            // Reparent to correct layer canvas
            var layerIndex = (int)panel.Layer;
            if (layerIndex >= 0 && layerIndex < _layers.Length && _layers[layerIndex] != null)
            {
                panel.transform.SetParent(_layers[layerIndex].transform, false);
            }

            // Start hidden
            panel.ApplyVisibilityOff();
        }

        // ===== Stack (Main / Popup) =====

        public async UniTask PushAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
            {
                Debug.LogError($"UIManager: Panel '{panelId}' not registered");
                return;
            }

            // Pause current top panel
            if (_stack.Count > 0)
            {
                var current = _stack.Peek();
                current.OnHide();
            }

            // Show new panel
            await ShowPanelAsync(panel);
            _stack.Push(panel);
        }

        public async UniTask PopAsync()
        {
            if (_stack.Count == 0) return;

            var panel = _stack.Peek();
            await HidePanelAsync(panel);
            _stack.Pop();

            // Resume underlying panel
            if (_stack.Count > 0)
            {
                var newTop = _stack.Peek();
                newTop.ApplyVisibilityOn();
                newTop.OnShow();
            }
        }

        public async UniTask PopTo(string panelId)
        {
            while (_stack.Count > 0 && _stack.Peek().PanelId != panelId)
            {
                await PopAsync();
            }
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
            _activeOverlays.Add(panel);
        }

        public async UniTask CloseAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
                return;

            await HidePanelAsync(panel);
            _activeOverlays.Remove(panel);
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

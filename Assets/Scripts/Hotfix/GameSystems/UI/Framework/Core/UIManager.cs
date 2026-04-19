using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Framework.Core
{
    /// <summary>
    /// Manages UI panel lifecycle, layers, and back navigation.
    /// Singleton accessible via UIManager.Instance.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        #region Singleton

        public static UIManager Instance { get; private set; }

        #endregion

        #region Layer Canvases

        private readonly Dictionary<int, Canvas> _layerCanvases = new();
        private readonly Dictionary<int, Transform> _layerParents = new();

        #endregion

        #region Panel Tracking

        private readonly Dictionary<Type, List<UIPanel>> _openPanels = new();
        private readonly Stack<UIPanel> _panelStack = new();
        private Action _defaultBackAction;

        #endregion

        #region Initialization

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeLayers();
        }

        private void InitializeLayers()
        {
            // Create canvas for each layer
            var layers = new (int layer, string name)[]
            {
                (UIConst.Layer_Base, UIConst.Canvas_Base),
                (UIConst.Layer_Main, UIConst.Canvas_Main),
                (UIConst.Layer_Popup, UIConst.Canvas_Popup),
                (UIConst.Layer_Guide, UIConst.Canvas_Guide),
                (UIConst.Layer_Toast, UIConst.Canvas_Toast),
            };

            foreach (var (layer, name) in layers)
            {
                CreateLayerCanvas(layer, name);
            }

            Debug.Log("UIManager initialized with layers");
        }

        private void CreateLayerCanvas(int layer, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = layer;
            canvas.pixelPerfect = true;

            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            _layerCanvases[layer] = canvas;
            _layerParents[layer] = go.transform;

            Debug.Log($"Created layer canvas: {name} (order: {layer})");
        }

        private Transform GetLayerParent(int layer)
        {
            // Find appropriate parent in layer range
            foreach (var kvp in _layerParents)
            {
                if (layer >= kvp.Key && layer < kvp.Key + 1000)
                {
                    return kvp.Value;
                }
            }
            return _layerParents[UIConst.Layer_Main]; // Default
        }

        #endregion

        #region Panel Operations

        /// <summary>
        /// Open a panel (type-based).
        /// Creates instance if not exists, or shows existing.
        /// </summary>
        public void Open<T>(params object[] args) where T : UIPanel
        {
            var type = typeof(T);

            // Check if already open (for single-instance panels)
            if (!_openPanels.TryGetValue(type, out var panels))
            {
                panels = new List<UIPanel>();
                _openPanels[type] = panels;
            }

            if (!panels.FirstOrDefault()?.CanMultiOpen ?? true)
            {
                // Single instance - show existing
                var existing = panels.FirstOrDefault();
                if (existing != null)
                {
                    ShowPanel(existing, args);
                    return;
                }
            }

            // Create new instance
            var panel = CreatePanel<T>();
            if (panel != null)
            {
                ShowPanel(panel, args);
            }
        }

        /// <summary>
        /// Close a panel (type-based).
        /// </summary>
        public void Close<T>() where T : UIPanel
        {
            var type = typeof(T);
            if (_openPanels.TryGetValue(type, out var panels))
            {
                var panel = panels.LastOrDefault();
                if (panel != null)
                {
                    ClosePanel(panel);
                }
            }
        }

        /// <summary>
        /// Close topmost panel.
        /// </summary>
        public void CloseTop()
        {
            if (_panelStack.Count > 0)
            {
                var panel = _panelStack.Pop();
                ClosePanel(panel);
            }
        }

        /// <summary>
        /// Close all panels.
        /// </summary>
        public void CloseAll()
        {
            foreach (var panels in _openPanels.Values)
            {
                foreach (var panel in panels.ToList())
                {
                    ClosePanel(panel, immediate: true);
                }
            }
            _openPanels.Clear();
            _panelStack.Clear();
        }

        private T CreatePanel<T>() where T : UIPanel
        {
            var prefabPath = GetPanelPrefabPath<T>();
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError($"Panel prefab path not found for {typeof(T).Name}");
                return null;
            }

            var prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"Panel prefab not found: {prefabPath}");
                return null;
            }

            var layer = GetPanelLayer<T>();
            var parent = GetLayerParent(layer);

            var go = Instantiate(prefab, parent);
            var panel = go.GetComponent<T>();

            if (panel == null)
            {
                Debug.LogError($"Prefab missing UIPanel component: {prefabPath}");
                Destroy(go);
                return null;
            }

            return panel;
        }

        private void ShowPanel(UIPanel panel, params object[] args)
        {
            var type = panel.GetType();

            // Set sort order
            var layer = GetPanelLayer(type);
            panel.SortOrder = GetNextSortOrder(layer);

            // Add to tracking
            if (!_openPanels.ContainsKey(type))
            {
                _openPanels[type] = new List<UIPanel>();
            }
            if (!_openPanels[type].Contains(panel))
            {
                _openPanels[type].Add(panel);
            }

            // Push to stack if blocking
            if (panel.BlockBack)
            {
                _panelStack.Push(panel);
            }

            // Show
            panel.Show(args);
        }

        private void ClosePanel(UIPanel panel, bool immediate = false)
        {
            var type = panel.GetType();

            // Remove from stack
            if (_panelStack.Contains(panel))
            {
                var stackList = _panelStack.ToList();
                stackList.Remove(panel);
                _panelStack.Clear();
                foreach (var p in stackList)
                {
                    _panelStack.Push(p);
                }
            }

            // Remove from tracking
            if (_openPanels.TryGetValue(type, out var panels))
            {
                panels.Remove(panel);
            }

            // Hide
            if (immediate)
            {
                panel.OnHide();
                panel.gameObject.SetActive(false);
            }
            else
            {
                panel.Hide(() =>
                {
                    Destroy(panel.gameObject);
                });
            }
        }

        private int GetNextSortOrder(int layer)
        {
            int maxOrder = layer;
            foreach (var panels in _openPanels.Values)
            {
                foreach (var panel in panels)
                {
                    if (panel.SortOrder > maxOrder)
                    {
                        maxOrder = panel.SortOrder;
                    }
                }
            }
            return maxOrder + 1;
        }

        #endregion

        #region Back Navigation

        /// <summary>
        /// Handle back button (Android/ESC).
        /// </summary>
        public void OnBackPressed()
        {
            if (_panelStack.Count > 0)
            {
                var topPanel = _panelStack.Peek();
                if (topPanel.BlockBack)
                {
                    CloseTop();
                    return;
                }
            }

            // Default action
            _defaultBackAction?.Invoke();
        }

        /// <summary>
        /// Set default back action (e.g., show main menu, exit app).
        /// </summary>
        public void SetDefaultBackAction(Action callback)
        {
            _defaultBackAction = callback ?? (() => { });
        }

        #endregion

        #region Panel Type Helpers

        private string GetPanelPrefabPath<T>() where T : UIPanel
        {
            // Create instance to get path (suboptimal but works)
            var panel = CreateInstance<T>();
            if (panel == null) return null;
            var path = panel.PrefabPath;
            Destroy(panel.gameObject);
            return path;
        }

        private int GetPanelLayer<T>() where T : UIPanel
        {
            var panel = CreateInstance<T>();
            if (panel == null) return UIConst.Layer_Main;
            var layer = panel.Layer;
            Destroy(panel.gameObject);
            return layer;
        }

        private int GetPanelLayer(Type type)
        {
            var panel = Activator.CreateInstance(type) as UIPanel;
            var layer = panel?.Layer ?? UIConst.Layer_Main;
            Destroy(panel?.gameObject);
            return layer;
        }

        private T CreateInstance<T>() where T : UIPanel
        {
            var type = typeof(T);
            var go = new GameObject(type.Name);
            return go.AddComponent<T>();
        }

        #endregion
    }
}

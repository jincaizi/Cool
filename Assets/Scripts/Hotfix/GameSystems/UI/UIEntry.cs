using System;
using System.Collections.Generic;
using Hotfix.GameSystems.UI.Framework.Core;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// UI system entry point.
    /// Initialize UIManager and pool on game start.
    /// </summary>
    public class UIEntry : MonoBehaviour
    {
        [Serializable]
        public class PoolConfig
        {
            public string PanelType;
            public string PrefabPath;
            public int PreLoadCount;
        }

        [Header("Pool Configuration")]
        [SerializeField] private List<PoolConfig> _poolConfigs = new();

        private static UIEntry _instance;
        public static UIEntry Instance => _instance;

        public UIManager Manager => UIManager.Instance;
        public UIPool Pool => _pool;

        private UIPool _pool;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        private void Initialize()
        {
            // Ensure UIManager exists
            if (UIManager.Instance == null)
            {
                var go = new GameObject("UIManager");
                go.AddComponent<UIManager>();
            }

            // Initialize pool
            _pool = new UIPool();

            // Register panels to pool
            foreach (var config in _poolConfigs)
            {
                if (string.IsNullOrEmpty(config.PrefabPath))
                    continue;

                RegisterPool(config.PrefabPath, config.PreLoadCount);
            }

            Debug.Log("UIEntry initialized");
        }

        private void RegisterPool(string prefabPath, int preLoadCount)
        {
            _pool.Register(prefabPath, preLoadCount);
        }

        /// <summary>
        /// Preload pools (call before showing first UI).
        /// </summary>
        public void Preload()
        {
            // Pool preloading is handled in Initialize
            Debug.Log("UI pools preloaded");
        }

        /// <summary>
        /// Shutdown UI system.
        /// </summary>
        public void Shutdown()
        {
            UIManager.Instance?.CloseAll();
            _pool = null;
            Debug.Log("UIEntry shutdown");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}

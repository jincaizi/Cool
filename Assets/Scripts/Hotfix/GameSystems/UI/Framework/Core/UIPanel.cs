using System;
using DG.Tweening;
using Hotfix.GameSystems.UI.Framework.Animation;
using Hotfix.GameSystems.UI.Framework.Binding;
using UnityEngine;

namespace Hotfix.GameSystems.UI.Framework.Core
{
    /// <summary>
    /// Base class for all UI panels.
    /// Provides lifecycle, animation, binding, and configuration.
    /// </summary>
    public abstract class UIPanel : MonoBehaviour
    {
        [Header("Panel Configuration")]
        [SerializeField] protected bool _canMultiOpen = true;
        [SerializeField] protected bool _closeOnClickOutside = false;
        [SerializeField] protected bool _blockBack = false;

        [Header("Animation Settings")]
        [SerializeField] protected bool _useOpenAnim = true;
        [SerializeField] protected bool _useCloseAnim = true;
        [SerializeField] protected float _openAnimDuration = 0.3f;
        [SerializeField] protected float _closeAnimDuration = 0.2f;

        protected UIDataBinding _binding;
        protected bool _isVisible;
        protected int _sortOrder;

        #region Properties

        /// <summary>
        /// Allow multiple instances of this panel.
        /// </summary>
        public bool CanMultiOpen
        {
            get => _canMultiOpen;
            set => _canMultiOpen = value;
        }

        /// <summary>
        /// Close panel when clicking background.
        /// </summary>
        public bool CloseOnClickOutside
        {
            get => _closeOnClickOutside;
            set => _closeOnClickOutside = value;
        }

        /// <summary>
        /// Block back button when visible.
        /// </summary>
        public bool BlockBack
        {
            get => _blockBack;
            set => _blockBack = value;
        }

        /// <summary>
        /// Current sort order in canvas.
        /// </summary>
        public int SortOrder
        {
            get => _sortOrder;
            set
            {
                _sortOrder = value;
                if (_canvasGroup != null)
                    _canvasGroup.sortingOrder = value;
            }
        }

        /// <summary>
        /// Is panel currently visible.
        /// </summary>
        public bool IsVisible => _isVisible;

        /// <summary>
        /// RectTransform cache.
        /// </summary>
        public RectTransform RectTransform => _rect;

        #endregion

        #region Abstract

        /// <summary>
        /// Prefab path for pool loading.
        /// </summary>
        protected abstract string PrefabPath { get; }

        /// <summary>
        /// Which canvas layer this panel belongs to.
        /// </summary>
        protected abstract int Layer { get; }

        #endregion

        #region Virtual Lifecycle

        /// <summary>
        /// Called before Show animation.
        /// Override for data preparation.
        /// </summary>
        protected virtual void OnPreShow(params object[] args) { }

        /// <summary>
        /// Called when panel is shown.
        /// Override for binding and initialization.
        /// </summary>
        protected virtual void OnShow(params object[] args) { }

        /// <summary>
        /// Called when panel is hidden.
        /// Override for cleanup.
        /// </summary>
        protected virtual void OnHide() { }

        /// <summary>
        /// Called when panel is destroyed.
        /// </summary>
        protected virtual void OnPanelDestroy() { }

        #endregion

        #region Animation Hooks

        /// <summary>
        /// Override for custom open animation.
        /// </summary>
        protected virtual void OnOpenAnimComplete() { }

        /// <summary>
        /// Override for custom close animation.
        /// </summary>
        protected virtual void OnCloseAnimComplete() { }

        /// <summary>
        /// Play open animation (called automatically unless disabled).
        /// </summary>
        protected virtual void PlayOpenAnim(Action onComplete)
        {
            if (!_useOpenAnim)
            {
                onComplete?.Invoke();
                return;
            }

            if (_rect == null)
            {
                onComplete?.Invoke();
                return;
            }

            var rect = RectTransform;
            rect.ScaleIn(_openAnimDuration, onComplete);
        }

        /// <summary>
        /// Play close animation (called automatically unless disabled).
        /// </summary>
        protected virtual void PlayCloseAnim(Action onComplete)
        {
            if (!_useCloseAnim)
            {
                onComplete?.Invoke();
                return;
            }

            if (_rect == null)
            {
                onComplete?.Invoke();
                return;
            }

            var rect = RectTransform;
            rect.ScaleOut(_closeAnimDuration, onComplete);
        }

        #endregion

        #region Binding

        /// <summary>
        /// Indexer for data binding.
        /// Usage: this["Health"] = () => vm.Health;
        /// </summary>
        protected object this[string key]
        {
            get => _binding?.GetValue(key);
            set => _binding?.NotifyChanged(key);
        }

        /// <summary>
        /// Bind to a ViewModel.
        /// </summary>
        protected void Bind(ViewModelBase vm)
        {
            Unbind();
            _binding = new UIDataBinding(vm);
        }

        /// <summary>
        /// Unbind from current ViewModel.
        /// </summary>
        protected void Unbind()
        {
            _binding?.Unbind();
            _binding = null;
        }

        /// <summary>
        /// Register a binding (called by panel for each bound property).
        /// </summary>
        protected void RegisterBinding(string key, Func<object> getter, Action<object> updater)
        {
            _binding?.Register(key, getter);
            if (updater != null)
            {
                _binding?.RegisterUpdater(key, updater);
            }
        }

        /// <summary>
        /// Refresh all bindings.
        /// </summary>
        protected void RefreshBindings()
        {
            _binding?.RefreshAll();
        }

        #endregion

        #region Internal

        private CanvasGroup _canvasGroup;
        private RectTransform _rect;
        private Canvas _canvas;

        // Called by UIManager to show panel
        internal void Show(params object[] args)
        {
            _isVisible = true;
            gameObject.SetActive(true);
            _rect?.KillAllTweens();
            OnPreShow(args);
            OnShow(args);

            PlayOpenAnim(() =>
            {
                OnOpenAnimComplete();
            });
        }

        // Called by UIManager to hide panel
        internal void Hide(Action onHideComplete)
        {
            PlayCloseAnim(() =>
            {
                OnCloseAnimComplete();
                OnHide();
                _isVisible = false;
                gameObject.SetActive(false);
                Unbind();
                onHideComplete?.Invoke();
            });
        }

        protected virtual void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _canvas = GetComponentInParent<Canvas>();
        }

        protected virtual void Start()
        {
            // Set initial state
            gameObject.SetActive(false);
        }

        protected virtual void OnDestroy()
        {
            OnPanelDestroy();
            Unbind();
        }

        #endregion
    }
}

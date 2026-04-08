using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.UI.Framework.Binding
{
    /// <summary>
    /// Data binding for UI panels using indexer pattern.
    /// Connects View to ViewModel without reflection overhead.
    /// </summary>
    public class UIDataBinding
    {
        private readonly ViewModelBase _viewModel;
        private readonly Dictionary<string, Func<object>> _getters = new();
        private readonly Dictionary<string, List<Action<object>>> _updateHandlers = new();

        public UIDataBinding(ViewModelBase viewModel)
        {
            _viewModel = viewModel;
        }

        /// <summary>
        /// Register a binding: key -> ViewModel property.
        /// </summary>
        public void Register(string key, Func<object> getter)
        {
            _getters[key] = getter;

            // Subscribe to ViewModel changes
            _viewModel.Subscribe(key, () => OnViewModelChanged(key));
        }

        /// <summary>
        /// Register an update handler (called when bound data changes).
        /// </summary>
        public void RegisterUpdater(string key, Action<object> updater)
        {
            if (!_updateHandlers.TryGetValue(key, out var list))
            {
                list = new List<Action<object>>();
                _updateHandlers[key] = list;
            }
            list.Add(updater);
        }

        /// <summary>
        /// Notify that a key changed (called from View via panel[key] = value).
        /// </summary>
        public void NotifyChanged(string key)
        {
            if (_updateHandlers.TryGetValue(key, out var handlers))
            {
                var value = _getters.TryGetValue(key, out var getter) ? getter?.Invoke() : _viewModel[key];
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler?.Invoke(value);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"UpdateHandler error for {key}: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Get current value for a key.
        /// </summary>
        public object GetValue(string key)
        {
            if (_getters.TryGetValue(key, out var getter))
            {
                return getter?.Invoke();
            }
            return _viewModel[key];
        }

        /// <summary>
        /// Get all values (for initial binding).
        /// </summary>
        public void RefreshAll()
        {
            foreach (var kvp in _getters)
            {
                NotifyChanged(kvp.Key);
            }
        }

        private void OnViewModelChanged(string key)
        {
            NotifyChanged(key);
        }

        /// <summary>
        /// Cleanup bindings.
        /// </summary>
        public void Unbind()
        {
            foreach (var key in _getters.Keys)
            {
                _viewModel.Unsubscribe(key, () => OnViewModelChanged(key));
            }
            _getters.Clear();
            _updateHandlers.Clear();
        }
    }
}

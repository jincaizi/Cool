using System;
using System.Collections.Generic;

namespace Hotfix.GameSystems.UI.Framework.Binding
{
    /// <summary>
    /// Base class for ViewModels in MVVM pattern.
    /// Provides indexer access and property change notification.
    /// </summary>
    public abstract class ViewModelBase : IDisposable
    {
        private readonly Dictionary<string, object> _properties = new();
        private readonly Dictionary<string, List<Action>> _changeHandlers = new();

        /// <summary>
        /// Indexer for binding access.
        /// </summary>
        public object this[string key]
        {
            get => GetProperty(key);
            set => SetProperty(key, value);
        }

        /// <summary>
        /// Set a property value and notify observers.
        /// </summary>
        protected void SetProperty(string key, object value)
        {
            _properties[key] = value;
            NotifyChanged(key);
        }

        /// <summary>
        /// Get a property value.
        /// </summary>
        protected object GetProperty(string key)
        {
            return _properties.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// Notify that a property changed.
        /// </summary>
        protected void NotifyChanged(string key)
        {
            if (_changeHandlers.TryGetValue(key, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler?.Invoke();
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogException(e);
                    }
                }
            }
        }

        /// <summary>
        /// Subscribe to property changes.
        /// </summary>
        public void Subscribe(string key, Action handler)
        {
            if (!_changeHandlers.TryGetValue(key, out var handlers))
            {
                handlers = new List<Action>();
                _changeHandlers[key] = handlers;
            }
            handlers.Add(handler);
        }

        /// <summary>
        /// Unsubscribe from property changes.
        /// </summary>
        public void Unsubscribe(string key, Action handler)
        {
            if (_changeHandlers.TryGetValue(key, out var handlers))
            {
                handlers.Remove(handler);
            }
        }

        /// <summary>
        /// Refresh data (called when panel shows).
        /// Override in subclass to reload data.
        /// </summary>
        public virtual void Refresh() { }

        /// <summary>
        /// Cleanup resources.
        /// </summary>
        public virtual void Dispose()
        {
            _properties.Clear();
            _changeHandlers.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Core.Resource;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>Owns the loaded prefab lease and the attachment for one active presentation.</summary>
    public sealed class ElementVfxAttachment
    {
        private readonly Func<string, CancellationToken, UniTask<GameObject>> _load;
        private readonly Action<string> _release;
        private readonly List<Renderer> _hiddenFallbacks = new();
        private CancellationTokenSource _pending;
        private GameObject _instance;
        private Transform _parent;
        private string _key;

        public ElementVfxAttachment() : this(Res.LoadAsync<GameObject>, Res.Release) { }

        internal ElementVfxAttachment(Func<string, CancellationToken, UniTask<GameObject>> load,
            Action<string> release)
        {
            _load = load;
            _release = release;
        }

        public bool IsVisible => _instance != null && _instance.activeInHierarchy;

        // Both the projectile pool and status events call Clear when the owner stops being visible.
        public void Show(string key, Transform parent, Renderer[] fallbackRenderers = null,
            Action<GameObject> prepareInstance = null)
        {
            if (key == _key && parent == _parent) return;
            Clear();
            if (string.IsNullOrEmpty(key) || parent == null) return;

            _key = key;
            _parent = parent;
            _pending = new CancellationTokenSource();
            AttachAsync(key, parent, fallbackRenderers, prepareInstance, _pending).Forget();
        }

        public void Clear()
        {
            var pending = _pending;
            _pending = null;
            pending?.Cancel();
            pending?.Dispose();

            foreach (var renderer in _hiddenFallbacks)
                if (renderer != null) renderer.enabled = true;
            _hiddenFallbacks.Clear();

            // The instance can already have been destroyed with its parent; its lease still belongs to us.
            if (_instance != null)
            {
                _instance.SetActive(false);
                if (Application.isPlaying)
                {
                    Object.Destroy(_instance);
                    ReleaseAfterDestroyAsync(_key).Forget();
                }
                else
                {
                    Object.DestroyImmediate(_instance);
                    _release(_key);
                }
            }
            else if (!ReferenceEquals(_instance, null))
            {
                _release(_key);
            }

            _instance = null;
            _parent = null;
            _key = null;
        }

        private async UniTask AttachAsync(string key, Transform parent, Renderer[] fallbackRenderers,
            Action<GameObject> prepareInstance, CancellationTokenSource request)
        {
            bool acquired = false;
            bool transferred = false;
            try
            {
                var prefab = await _load(key, request.Token);
                acquired = true;
                if (_pending != request || prefab == null || parent == null || !parent.gameObject.activeInHierarchy)
                    return;

                _instance = Object.Instantiate(prefab, parent, false);
                transferred = true;
                prepareInstance?.Invoke(_instance);
                if (fallbackRenderers == null) return;

                foreach (var renderer in fallbackRenderers)
                {
                    if (renderer == null || !renderer.enabled) continue;
                    renderer.enabled = false;
                    _hiddenFallbacks.Add(renderer);
                }
            }
            catch (OperationCanceledException) { }
            catch (ResourceLoadException)
            {
                // The built-in visual remains available while a catalog entry or bundle is missing.
            }
            finally
            {
                if (acquired && !transferred) _release(key);
                if (_pending == request)
                {
                    _pending = null;
                    request.Dispose();
                }
            }
        }

        private async UniTask ReleaseAfterDestroyAsync(string key)
        {
            await UniTask.NextFrame();
            _release(key);
        }
    }
}

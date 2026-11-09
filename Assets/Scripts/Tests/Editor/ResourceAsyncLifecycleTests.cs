using System;
using System.Collections.Generic;
using System.Threading;
using Core.Resource;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;
using AssetManager = Core.Resource.ResourceManager;

namespace GameSys.EditorTests
{
    public class ResourceAsyncLifecycleTests
    {
        // Replace only external IO: these are real Addressables operations with real release/destroy semantics.
        private sealed class ControlledOperation : AsyncOperationBase<Object>
        {
            public int DestroyCount;
            public bool Finished;
            protected override void Execute() { }
            public void Finish(Object asset, Exception error = null)
            {
                Finished = true;
                Complete(asset, error == null, error);
            }
            protected override void Destroy() => DestroyCount++;
        }

        private sealed class ControlledBackend : IResourceAssetBackend
        {
            public readonly UnityEngine.ResourceManagement.ResourceManager Operations = new UnityEngine.ResourceManagement.ResourceManager();
            public readonly List<ControlledOperation> Loads = new List<ControlledOperation>();
            public int Releases;
            public AsyncOperationHandle Load<T>(string key) where T : Object
            {
                var operation = new ControlledOperation();
                Loads.Add(operation);
                return Operations.StartOperation(operation, default);
            }
            public void Release(AsyncOperationHandle handle)
            {
                Releases++;
                Operations.Release(handle);
            }
        }

        private const string Key = "controlled-resource";
        private ControlledBackend _backend;
        private AssetManager _manager;
        private GameObject _asset;
        private Action<AsyncOperationHandle, Exception> _exceptionHandler;

        [SetUp]
        public void SetUp()
        {
            _backend = new ControlledBackend();
            _manager = new AssetManager(_backend);
            _asset = new GameObject(Key);
            _exceptionHandler = UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler;
            UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler = (_, __) => { };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var operation in _backend.Loads)
                if (!operation.Finished) operation.Finish(_asset);
            for (int i = 0; i < 20 && _manager.GetRefCount(Key) > 0; i++) _manager.Release(Key);
            _backend.Operations.Dispose();
            UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler = _exceptionHandler;
            Object.DestroyImmediate(_asset);
        }

        private static T Result<T>(UniTask<T> request)
        {
            Assert.AreNotEqual(UniTaskStatus.Pending, request.Status, "controlled operation must finish its consumer");
            return request.GetAwaiter().GetResult();
        }

        // Catches publishing the cache after completion, resulting in duplicate flights/lost consumer refs.
        [Test]
        public void SameKeyConcurrentConsumersBalanceEveryReference()
        {
            var first = _manager.LoadAsync<GameObject>(Key);
            var second = _manager.LoadAsync<GameObject>(Key);
            Assert.AreEqual(1, _backend.Loads.Count);
            _backend.Loads[0].Finish(_asset);
            Assert.AreSame(_asset, Result(first));
            Assert.AreSame(_asset, Result(second));
            Assert.AreEqual(2, _manager.GetRefCount(Key));
            _manager.Release(Key);
            Assert.AreEqual(0, _backend.Releases);
            _manager.Release(Key);
            Assert.AreEqual(1, _backend.Releases);
            Assert.AreEqual(1, _backend.Loads[0].DestroyCount);
            Assert.AreEqual(-1, _manager.GetRefCount(Key));
        }

        [Test]
        public void BaseThenDerivedConsumersShareTheActualAsset()
        {
            var first = _manager.LoadAsync<Object>(Key);
            var second = _manager.LoadAsync<GameObject>(Key);
            Assert.AreEqual(1, _backend.Loads.Count);
            _backend.Loads[0].Finish(_asset);
            Assert.AreSame(_asset, Result(first));
            Assert.AreSame(_asset, Result(second));
            Assert.AreSame(_asset, Result(_manager.LoadAsync<GameObject>(Key)));
            Assert.AreEqual(3, _manager.GetRefCount(Key));
        }

        [Test]
        public void IncompatibleConsumerDoesNotAcquireReference()
        {
            var first = _manager.LoadAsync<GameObject>(Key);
            var incompatible = _manager.LoadAsync<Texture2D>(Key);
            Assert.Throws<ResourceLoadException>(() => Result(incompatible));
            Assert.AreEqual(1, _backend.Loads.Count);
            _backend.Loads[0].Finish(_asset);
            Assert.AreSame(_asset, Result(first));
            Assert.AreEqual(1, _manager.GetRefCount(Key));
        }

        [Test]
        public void FailedSharedLoadIsReleasedEvictedAndCanRetry()
        {
            var first = _manager.LoadAsync<GameObject>(Key);
            var second = _manager.LoadAsync<GameObject>(Key);
            _backend.Loads[0].Finish(null, new InvalidOperationException("controlled failure"));
            Assert.Throws<ResourceLoadException>(() => Result(first));
            Assert.Throws<ResourceLoadException>(() => Result(second));
            Assert.AreEqual(1, _backend.Releases);
            Assert.AreEqual(1, _backend.Loads[0].DestroyCount);
            Assert.AreEqual(-1, _manager.GetRefCount(Key));
            var retry = _manager.LoadAsync<GameObject>(Key);
            Assert.AreEqual(2, _backend.Loads.Count);
            _backend.Loads[1].Finish(_asset);
            Assert.AreSame(_asset, Result(retry));
            Assert.AreEqual(1, _manager.GetRefCount(Key));
            _manager.Release(Key);
            Assert.AreEqual(2, _backend.Releases);
        }

        [Test]
        public void CancelOneConsumerKeepsSharedOperationAndOtherReference()
        {
            using var cancel = new CancellationTokenSource();
            var canceled = _manager.LoadAsync<GameObject>(Key, cancel.Token);
            var survivor = _manager.LoadAsync<GameObject>(Key);
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => Result(canceled));
            Assert.AreEqual(0, _backend.Releases);
            _backend.Loads[0].Finish(_asset);
            Assert.AreSame(_asset, Result(survivor));
            Assert.AreEqual(1, _manager.GetRefCount(Key));
            _manager.Release(Key);
            Assert.AreEqual(1, _backend.Releases);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancelAllConsumersObservesLateCompletionAndReleasesOnce(bool fail)
        {
            using var cancel = new CancellationTokenSource();
            var request = _manager.LoadAsync<GameObject>(Key, cancel.Token);
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => Result(request));
            Assert.AreEqual(0, _backend.Releases);
            _backend.Loads[0].Finish(fail ? null : _asset, fail ? new Exception("late failure") : null);
            Assert.AreEqual(1, _backend.Releases);
            Assert.AreEqual(1, _backend.Loads[0].DestroyCount);
            Assert.AreEqual(-1, _manager.GetRefCount(Key));
        }

        [Test]
        public void PreCanceledRequestDoesNotStartOperation()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => Result(_manager.LoadAsync<GameObject>(Key, cancel.Token)));
            Assert.AreEqual(0, _backend.Loads.Count);
        }

        [Test]
        public void BaseLoadRejectsWrongDerivedResultWithoutLosingSurvivor()
        {
            var first = _manager.LoadAsync<Object>(Key);
            var incompatible = _manager.LoadAsync<Texture2D>(Key);
            _backend.Loads[0].Finish(_asset);
            Assert.AreSame(_asset, Result(first));
            Assert.Throws<ResourceLoadException>(() => Result(incompatible));
            Assert.AreEqual(1, _manager.GetRefCount(Key));
        }
    }
}

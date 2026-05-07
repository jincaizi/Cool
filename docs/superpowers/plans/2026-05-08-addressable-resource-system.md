# Addressable Resource Management System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a lightweight Addressables wrapper in AOT/Core with reference-counted lifecycle tracking, UniTask-based async loading, and a clean static API.

**Architecture:** `Res` static class → `ResourceManager` singleton (Dictionary<string, HandleEntry>) → Addressables API. All code lives in `Assets/Scripts/AOT/Core/Resource/` under a new `Core` assembly definition.

**Tech Stack:** Unity 2022.3 LTS, Addressables 1.21.21, UniTask

**Prerequisites:** UniTask package must be installed before any code compiles.

---

### Task 1: Install UniTask Package

**Files:**
- Modify: `Packages/manifest.json`

Install UniTask via Unity Package Manager. Use the official GitHub repository URL for the Unity 2022-compatible version.

- [ ] **Step 1: Add UniTask to manifest.json**

Open `Packages/manifest.json` and add the following entry to the `"dependencies"` block:

```json
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
```

Save the file. Unity will automatically detect the change and resolve the package.

- [ ] **Step 2: Verify package resolved**

Run compilation check via Unity console:

```bash
# Wait for Unity to resolve the package (check Package Manager or console)
# Look for compilation errors - there should be none from UniTask itself
```

Manual check: Open Unity Editor → Window → Package Manager → expect UniTask in the installed packages list.

- [ ] **Step 3: Commit**

```bash
git add Packages/manifest.json
git commit -m "$(cat <<'EOF'
chore: add UniTask package dependency
EOF
)"
```

---

### Task 2: Create Core Assembly Definition

**Files:**
- Create: `Assets/Scripts/AOT/Core/Core.asmdef`

The `AOT/Core/` directory is empty and has no assembly definition. We need one so Resource code compiles as a standalone assembly. The Hotfix layer will reference this assembly.

- [ ] **Step 1: Create Core.asmdef**

```json
{
    "name": "Core",
    "rootNamespace": "Core",
    "references": [
        "UniTask"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Note: `"UniTask"` in references is needed because the ResourceManager will use `UniTask<T>`. Verify the exact assembly name by checking `Assets/Plugins/UniTask/...asmdef` once UniTask is installed. If UniTask's assembly is named differently (e.g., `UniTask.Addressables`), adjust accordingly. Actually, the UniTask core assembly is `UniTask` — confirmed.

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/AOT/Core/Core.asmdef
git commit -m "$(cat <<'EOF'
chore: add Core assembly definition for AOT infrastructure
EOF
)"
```

---

### Task 3: Create HandleEntry (Internal Data Structure)

**Files:**
- Create: `Assets/Scripts/AOT/Core/Resource/HandleEntry.cs`

A simple internal struct holding the Addressables handle and ref count for each loaded asset.

- [ ] **Step 1: Create HandleEntry.cs**

```csharp
using System;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Core.Resource
{
    internal sealed class HandleEntry
    {
        public AsyncOperationHandle Handle;
        public int RefCount;
        public Type AssetType;
        public string Key;

        public HandleEntry(AsyncOperationHandle handle, Type assetType, string key)
        {
            Handle = handle;
            AssetType = assetType;
            Key = key;
            RefCount = 1;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/AOT/Core/Resource/HandleEntry.cs Assets/Scripts/AOT/Core/Resource/HandleEntry.cs.meta
git commit -m "$(cat <<'EOF'
feat: add HandleEntry for reference-counted handle tracking
EOF
)"
```

---

### Task 4: Create ResourceLoadException

**Files:**
- Create: `Assets/Scripts/AOT/Core/Resource/ResourceLoadException.cs`

Custom exception with the failed key for debugging.

- [ ] **Step 1: Create ResourceLoadException.cs**

```csharp
using System;

namespace Core.Resource
{
    public class ResourceLoadException : Exception
    {
        public string Key { get; }

        public ResourceLoadException(string key, Exception inner)
            : base($"Failed to load resource: {key}", inner)
        {
            Key = key;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/AOT/Core/Resource/ResourceLoadException.cs Assets/Scripts/AOT/Core/Resource/ResourceLoadException.cs.meta
git commit -m "$(cat <<'EOF'
feat: add ResourceLoadException for resource load failures
EOF
)"
```

---

### Task 5: Create ResourceManager

**Files:**
- Create: `Assets/Scripts/AOT/Core/Resource/ResourceManager.cs`

The singleton that owns the handle dictionary and ref-counting logic. This is the core of the system.

- [ ] **Step 1: Create ResourceManager.cs**

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Core.Resource
{
    internal sealed class ResourceManager
    {
        private static ResourceManager _instance;
        internal static ResourceManager Instance => _instance ?? (_instance = new ResourceManager());

        private readonly Dictionary<string, HandleEntry> _handles = new Dictionary<string, HandleEntry>();

        private ResourceManager() { }

        // ===== Async Load =====

        internal async UniTask<T> LoadAsync<T>(string key) where T : UnityEngine.Object
        {
            return await LoadInternalAsync<T>(key);
        }

        internal async UniTask<T> LoadAsync<T>(AssetReference reference) where T : UnityEngine.Object
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            return await LoadInternalAsync<T>(reference.RuntimeKey.ToString());
        }

        private async UniTask<T> LoadInternalAsync<T>(string key) where T : UnityEngine.Object
        {
            if (_handles.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return (T)entry.Handle.Result;
            }

            try
            {
                var handle = Addressables.LoadAssetAsync<T>(key);
                await handle.Task;

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    _handles[key] = new HandleEntry(handle, typeof(T), key);
                    return handle.Result;
                }

                throw new ResourceLoadException(key,
                    new Exception($"Addressables load failed with status: {handle.Status}"));
            }
            catch (ResourceLoadException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ResourceLoadException(key, e);
            }
        }

        // ===== Sync Load =====

        internal T Load<T>(string key) where T : UnityEngine.Object
        {
            return LoadInternalSync<T>(key);
        }

        internal T Load<T>(AssetReference reference) where T : UnityEngine.Object
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            return LoadInternalSync<T>(reference.RuntimeKey.ToString());
        }

        private T LoadInternalSync<T>(string key) where T : UnityEngine.Object
        {
            if (_handles.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return (T)entry.Handle.Result;
            }

            try
            {
                var handle = Addressables.LoadAssetAsync<T>(key);
                handle.WaitForCompletion();

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    _handles[key] = new HandleEntry(handle, typeof(T), key);
                    return handle.Result;
                }

                throw new ResourceLoadException(key,
                    new Exception($"Addressables load failed with status: {handle.Status}"));
            }
            catch (ResourceLoadException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ResourceLoadException(key, e);
            }
        }

        // ===== Release =====

        internal void Release(string key)
        {
            if (!_handles.TryGetValue(key, out var entry))
                return;

            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                Addressables.Release(entry.Handle);
                _handles.Remove(key);
            }
        }

        internal void Release(AssetReference reference)
        {
            if (reference == null)
                return;
            Release(reference.RuntimeKey.ToString());
        }

        // ===== Scene =====

        internal async UniTask LoadSceneAsync(string key, LoadSceneMode mode)
        {
            var handle = Addressables.LoadSceneAsync(key, mode);
            await handle.Task;

            if (handle.Status != AsyncOperationStatus.Succeeded)
                throw new ResourceLoadException(key,
                    new Exception($"Scene load failed with status: {handle.Status}"));
        }

        internal async UniTask UnloadSceneAsync(string key)
        {
            var handle = Addressables.UnloadSceneAsync(key);
            await handle.Task;
        }

        // ===== Debug =====

        internal int GetRefCount(string key)
        {
            return _handles.TryGetValue(key, out var entry) ? entry.RefCount : -1;
        }

        internal IReadOnlyList<string> GetLoadedKeys()
        {
            return new List<string>(_handles.Keys);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/AOT/Core/Resource/ResourceManager.cs Assets/Scripts/AOT/Core/Resource/ResourceManager.cs.meta
git commit -m "$(cat <<'EOF'
feat: add ResourceManager with ref-counted load/release tracking
EOF
)"
```

---

### Task 6: Create Res Static Entry

**Files:**
- Create: `Assets/Scripts/AOT/Core/Resource/Res.cs`

The public API — all business code calls through this static class.

- [ ] **Step 1: Create Res.cs**

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace Core.Resource
{
    public static class Res
    {
        // ===== Async Load =====

        public static UniTask<T> LoadAsync<T>(string key) where T : UnityEngine.Object
        {
            return ResourceManager.Instance.LoadAsync<T>(key);
        }

        public static UniTask<T> LoadAsync<T>(AssetReference reference) where T : UnityEngine.Object
        {
            return ResourceManager.Instance.LoadAsync<T>(reference);
        }

        // ===== Sync Load =====

        public static T Load<T>(string key) where T : UnityEngine.Object
        {
            return ResourceManager.Instance.Load<T>(key);
        }

        public static T Load<T>(AssetReference reference) where T : UnityEngine.Object
        {
            return ResourceManager.Instance.Load<T>(reference);
        }

        // ===== Release =====

        public static void Release(string key)
        {
            ResourceManager.Instance.Release(key);
        }

        public static void Release(AssetReference reference)
        {
            ResourceManager.Instance.Release(reference);
        }

        // ===== Scene =====

        public static UniTask LoadSceneAsync(string key, LoadSceneMode mode = LoadSceneMode.Single)
        {
            return ResourceManager.Instance.LoadSceneAsync(key, mode);
        }

        public static UniTask UnloadSceneAsync(string key)
        {
            return ResourceManager.Instance.UnloadSceneAsync(key);
        }

        // ===== Debug =====

        public static int GetRefCount(string key)
        {
            return ResourceManager.Instance.GetRefCount(key);
        }

        public static IReadOnlyList<string> GetLoadedKeys()
        {
            return ResourceManager.Instance.GetLoadedKeys();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/AOT/Core/Resource/Res.cs Assets/Scripts/AOT/Core/Resource/Res.cs.meta
git commit -m "$(cat <<'EOF'
feat: add Res static entry point for resource management
EOF
)"
```

---

### Task 7: Verify Compilation

**Files:** None (verification only)

After all files are in place, Unity should compile without errors.

- [ ] **Step 1: Refresh AssetDatabase and check compilation**

```
Open Unity Editor. The AssetDatabase will auto-refresh and trigger compilation.
```

Check console for errors:
- Expected: 0 compilation errors
- Common issues: UniTask assembly name mismatch (adjust `references` in Core.asmdef), or Addressables not installed

- [ ] **Step 2: Fix any compilation issues**

If UniTask assembly can't be found, verify the assembly name:
```bash
find Assets/Plugins/UniTask -name "*.asmdef" -exec grep "name" {} \;
```

If the assembly name is different from `"UniTask"`, update `Core.asmdef` references accordingly.

- [ ] **Step 3: Commit (if fixes were needed)**

```bash
git add -A
git commit -m "$(cat <<'EOF'
fix: resolve compilation issues for Resource system
EOF
)"
```

---

### Approval Gate

**Is the plan complete?** — After Task 7, the Resource system compiles. All four files exist, the API matches the design spec, and the system is ready for business code to use.

**What this system does NOT cover (out of scope):**
- Addressable Groups configuration (Unity Editor manual work)
- Object pooling / GameObject instantiation
- Migration of existing `Resources.Load` calls (separate follow-up work)
- PlayMode tests (requires Addressables test setup, separate effort)
- Loading progress reporting (can be added later without API changes)

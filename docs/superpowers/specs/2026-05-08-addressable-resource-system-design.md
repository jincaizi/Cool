# Addressable Resource Management System Design

**Date:** 2026-05-08
**Engine:** Unity 2022.3.25f1
**Layer:** AOT (`Assets/Scripts/AOT/Core/`)
**Async Model:** UniTask

## 1. Motivation

- Addressables 包已安装 (v1.21.21) 但未配置和使用，当前全部资源走同步 `Resources.Load` + `Object.Instantiate`
- 首包最小化要求：所有资源通过 Addressables 按需异步加载/卸载
- 无生命周期追踪：无去重加载、无引用计数、无内存追踪，多人协作容易重复加载或忘记释放
- AOT/Core 目前为空，作为底层基础设施正合适

## 2. Design Overview

在 Addressables API 之上构建带生命周期追踪的轻量资源管理器。核心组件：

```
┌──────────────────────────────────┐
│         Res (static entry)       │  ← 业务代码唯一入口
├──────────────────────────────────┤
│       ResourceManager (singleton)│  ← 内部：字典 + 引用计数
├──────────────────────────────────┤
│       Addressables API           │  ← Unity 原生
└──────────────────────────────────┘
```

**职责边界：** 资源系统只管 Asset（ScriptableObject、Texture、AudioClip、Prefab 等）的加载和卸载。GameObject 实例化和对象池不属于本系统。

## 3. Core API

```csharp
// === Async (UniTask) ===

/// <summary>异步加载资源，已加载的自动去重，引用计数 +1</summary>
public static UniTask<T> LoadAsync<T>(string key) where T : UnityEngine.Object;
public static UniTask<T> LoadAsync<T>(AssetReference reference) where T : UnityEngine.Object;

/// <summary>异步加载场景</summary>
public static UniTask LoadSceneAsync(string key, LoadSceneMode mode = LoadSceneMode.Single);

/// <summary>异步卸载场景</summary>
public static UniTask UnloadSceneAsync(string key);

// === Sync (仅小资源) ===

/// <summary>同步加载小资源，内部 WaitForCompletion</summary>
public static T Load<T>(string key) where T : UnityEngine.Object;
public static T Load<T>(AssetReference reference) where T : UnityEngine.Object;

// === Release ===

/// <summary>释放资源，引用计数归零时真正 Release</summary>
public static void Release(string key);
public static void Release(AssetReference reference);

// === Debug ===

/// <summary>获取资源当前引用计数（-1 表示未加载）</summary>
public static int GetRefCount(string key);

/// <summary>获取当前所有已加载资源的 key 列表</summary>
public static IReadOnlyList<string> GetLoadedKeys();
```

## 4. Handle Management

```csharp
internal class HandleEntry
{
    public AsyncOperationHandle Handle;   // Addressables handle
    public int RefCount;                  // 当前引用计数
    public Type AssetType;                // 加载时的类型
    public string Key;                    // address
}
```

- `ResourceManager` 内部维护 `Dictionary<string, HandleEntry>`，key 为 address
- **加载:** key 已存在 → RefCount++，直接返回 `Handle.Result`；不存在 → `Addressables.LoadAssetAsync`，结果存入字典，RefCount = 1
- **释放:** RefCount-- → 归零时 `Addressables.Release(handle)` 并从字典移除；未归零则只减计数
- **同步加载:** 内部仍走 `Addressables.LoadAssetAsync<T>().WaitForCompletion()`，字典管理逻辑与异步一致
- **场景:** 场景 handle 不进入该字典，场景的加载/卸载直接对应 handle 的获取/释放，不参与引用计数

## 5. Error Handling

```csharp
public class ResourceLoadException : Exception
{
    public string Key { get; }
}
```

- `LoadAsync` / `Load` 失败时抛出 `ResourceLoadException`（包含 key），业务层决定重试、降级或提示
- 加载失败不占坑字典，不污染后续重试
- `Release` 对不存在的 key 静默忽略（不抛异常）

## 6. Addressables Configuration

不在本系统代码中完成，需在 Unity Editor 中手动配置：
- 资源标记 Addressable label
- 组织 Addressable Groups（按模块/场景划分 group）
- 首包场景设置为 Scene In Build，其余通过 Addressables 远程/本地加载

## 7. File Structure

```
Assets/Scripts/AOT/Core/Resource/
├── Res.cs                    // 静态入口
├── ResourceManager.cs        // 单例，字典 + 引用计数
├── HandleEntry.cs            // 内部数据结构
└── ResourceLoadException.cs  // 异常类
```

## 8. Unresolved Items

- `AssetReference` 支持：需评估 `AsyncOperationHandle` 对 `AssetReference` 的泛型转换是否稳定，如有问题首版可暂只支持 string key
- 前置依赖：需先引入 UniTask 包 (`com.cysharp.unitask`)
- 与现有 `Resources.LoadAll<SkillConfig>` 的迁移路径：先建系统，后逐步迁移各模块

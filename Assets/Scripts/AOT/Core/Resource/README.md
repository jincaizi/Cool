# Core/Resource —— Addressable 资源加载

| 文件 | 作用 |
|---|---|
| `ResourceManager.cs` | 内部单例：异步加载、实例化、场景加载，句柄去重 |
| `Res.cs` | 对外静态门面 |
| `HandleEntry.cs` | 加载句柄与引用计数 |
| `ResourceLoadException.cs` | 加载异常 |

约定：高频对象配合对象池；Addressables key 集中在 `DataDefinition/GameConsts`；
业务代码通过 `Res` 调用，不要直接使用 `Addressables`。

## 相关模块

- [DataDefinition](../../DataDefinition/README.md) —— 常量定义
- [UI](../../../Hotfix/GameSystems/UI/README.md) —— 面板预制体加载

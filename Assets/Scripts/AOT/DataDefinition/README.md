# DataDefinition —— 全局常量与设置

- `GameConsts.cs`：全局常量（Addressables key、层名、Shader 属性 ID 等）。
- `GameSettings.cs`：全局设置。UI 倍率：`UIScale` 是叠加在平台默认之上的全局倍率，`MobilePlatformUIScale`（默认 1.15）与 `DesktopPlatformUIScale`（默认 1）是平台默认倍率，`ResolveUIScale()` 合并后钳在 `[0.8, 1.5]`。

约束：AOT 程序集，不得依赖 Hotfix。跨模块常量放这里，模块私有常量留在模块内。

## 相关模块

- [资源](../Core/Resource/README.md) —— Addressables 加载
- [KCP](../KcpNet/README.md) —— 网络常量

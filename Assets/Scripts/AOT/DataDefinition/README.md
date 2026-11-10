# DataDefinition —— 全局常量与设置

- `GameConsts.cs`：全局常量（Addressables key、层名、Shader 属性 ID 等）。
- `GameSettings.cs`：全局设置。UI 倍率：`UIScale` 是叠加在平台默认之上的全局倍率，`MobilePlatformUIScale`（默认 1.15）与 `DesktopPlatformUIScale`（默认 1）是平台默认倍率，`ResolveUIScale()` 合并后钳在 `[0.8, 1.5]`。
- `BuildVersion.cs`：构建版本号的运行时门面。`Raw` 是 `Application.version`，`Display` 加 `v` 前缀；`IsBaseVersion` / `Compose` 供出包脚本使用。版本规则：`a` 大版本 / 里程碑 / 不兼容改动，`b` 中等更新 / 新系统 / 新玩法，`c` 小迭代 / 修复 / 数值与表现调整；WebGL/Windows 出包时临时追加 git 短 SHA 后 4 位。

约束：AOT 程序集，不得依赖 Hotfix。跨模块常量放这里，模块私有常量留在模块内。

## 相关模块

- [资源](../Core/Resource/README.md) —— Addressables 加载
- [KCP](../KcpNet/README.md) —— 网络常量

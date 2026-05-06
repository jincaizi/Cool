# GameSystems 程序集拆分设计方案

**日期:** 2026-05-06
**状态:** 已批准

## 背景

GameSystems 目前存在以下问题：
- Bag、Combat、Monster、NpcMirror 模块混在未独立程序集中
- Combat 与 Monster 存在循环依赖（PlayerHitZone → MonsterAttackHitbox → Monster → Combat）
- 模块间耦合度高，不利于编译优化和热重载粒度控制

**目标:**
- A: 编译速度优化（程序集并行编译）
- B: 模块解耦（各模块独立演进）
- C: HybridCLR 热重载粒度控制

## 设计决策

### 决策 1: Core 层扩展

在 `Sys3C.Core` 中新增 `Combat/` 共享接口层，打破模块间的循环依赖：

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/
├── IDamageable.cs       (从 Combat 移入)
├── IAttackHitbox.cs     (新增，抽象 AttackHitbox)
└── AttackHitboxData.cs  (新增，共享数据结构)
```

所有模块引用 Core，不直接互相引用，循环依赖自然消除。

### 决策 2: 模块 asmdef 划分

为每个子目录创建独立 asmdef：

| 程序集 | 引用关系 | 现有状态 |
|--------|---------|---------|
| `Hotfix.GameSystems.Sys3C.Core` | 无 | ✅ 已有（扩展） |
| `Hotfix.GameSystems.Skills` | 无 | ✅ 已有 |
| `Hotfix.GameSystems.UI` | KcpNet, DOTween | ✅ 已有 |
| `Hotfix.GameSystems.Sys3C` | Core, Skills | ✅ 已有 |
| `Hotfix.GameSystems.Bag` | 无 | ❌ 新增 |
| `Hotfix.GameSystems.Combat` | Core | ❌ 新增 |
| `Hotfix.GameSystems.Monster` | Core, Combat | ❌ 新增 |
| `Hotfix.GameSystems.NpcMirror` | 无 | ❌ 新增 |

### 决策 3: HybridCLR AOT/Hotfix 边界

```
AOT 层（稳定，编译后不热重载）
├── Hotfix.GameSystems.Sys3C.Core
├── Hotfix.GameSystems.Combat
└── Hotfix.GameSystems.Bag.Data (Bag 模块数据部分)

Hotfix 层（频繁变更）
├── Hotfix.GameSystems.Sys3C
├── Hotfix.GameSystems.Skills
├── Hotfix.GameSystems.UI
├── Hotfix.GameSystems.Bag.Runtime
├── Hotfix.GameSystems.Monster
└── Hotfix.GameSystems.NpcMirror
```

**原则:**
- AOT: 接口、枚举、纯数据结构、基本算法
- Hotfix: 状态机、UI 逻辑、AI 行为、可配置逻辑

## 实现步骤

1. **扩展 Core 层:**
   - 创建 `Combat/` 目录
   - 移动 `IDamageable.cs` 到 `Core/Combat/`
   - 新增 `IAttackHitbox.cs`
   - 新增 `AttackHitboxData.cs`（从 Combat 移动）

2. **创建新 asmdef:**
   - `Bag.asmdef`
   - `Combat.asmdef`
   - `Monster.asmdef`
   - `NpcMirror.asmdef`

3. **更新依赖引用:**
   - 修改相关 using 语句指向新的命名空间路径
   - 更新各 asmdef 的 references 配置

4. **修复循环依赖:**
   - `PlayerHitZone` 改用 Core 中的接口
   - `MonsterHitZone` 改用 Core 中的接口

5. **配置 HybridCLR:**
   - 标记 AOT 程序集排除热重载
   - 验证各模块正常加载

## 影响范围

- 移动文件: 3-4 个接口文件
- 新增文件: 2 个接口/数据结构
- 修改文件: ~15 个 using 语句和引用配置
- 无运行时行为变更，仅架构调整
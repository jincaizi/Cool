# 项目总览

## 1. 项目概述
   基于Unity 2022 LTS的MMO客户端，服务端后续接入。当前聚焦客户端主体开发，代码由Claude Code协助生成。

## 2. 技术栈
- Unity版本：2022 LTS
- 网络通信：KCP + Protobuf
- 资源管理：Addressable
- 热更新：HybridCLR（代码热更）
- UI框架：基于UGUI的自定义框架

---

## 3. 核心模块

### 3.1 3C（Character, Camera, Control）
- 角色：ECS/GameObject模式，动画状态机，本地移动+客户端预测
- 相机：第三人称跟随（Cinemachine），支持旋转/缩放/碰撞
- 控制：新输入系统，统一移动/交互/技能操作

### 3.2 登录模块
- 账号认证、角色创建/选择、服务器列表
- 网络连接（KCP），token自动登录

### 3.3 背包模块
- 网格背包，拖拽/堆叠/整理/快捷栏
- 道具配置表（ScriptableObject或Json）
- 本地持久化，网络同步预留

### 3.4 武器系统
- 装备槽位，武器类型影响攻击动画/伤害
- 动态挂载模型，攻击逻辑（本地判定，后续服务端验证）

### 3.5 音效模块
- AudioMixer分组，背景音乐/音效独立控制
- 动态播放（Addressable加载）

---

## 4. 代码组织与热更新（HybridCLR）

采用AOT + 热更新分层，通过接口解耦。

### 4.1 程序集划分

AOT层：
- Core（主工程）：事件、资源、输入、对象池等底层
- Network（主工程）：KCP通信、Protobuf
- Data.Definition（主工程）：数据接口、枚举、事件常量
- Bridge（主工程）：桥接接口，供热更新调用AOT功能
- HybridCLR.Loader（主工程）：加载热更新DLL，初始化热更新

热更新层（独立DLL）：
- GameSystems.Hotfix：3C、背包、武器等游戏系统实现
- UI.Hotfix：UI面板、控制器
- Entry：热更新入口，启动游戏逻辑

### 4.2 目录结构
```text
  Assets/Scripts/
  ├── AOT/                                 # AOT层代码（IL2CPP编译，不可热更）
  │   ├── Core/                            # Core - 事件、资源、输入、对象池等
  │   ├── Network/                         # Network - KCP通信、Protobuf
  │   ├── DataDefinition/                  # Data.Definition - 接口、枚举、常量
  │   ├── Bridge/                          # Bridge - 桥接接口层
  │   └── HybridCLRLoader/                 # HybridCLR.Loader - 热更新DLL加载器
  │
  └── Hotfix/                              # 热更新层源码（打包为独立DLL）
      ├── GameSystems/                     # GameSystems.Hotfix - 背包、武器等
      │   ├── Sys3C                        # 3C系统 
      │   └── Bag                          # 背包
      ├── UI/                              # UI.Hotfix - UI面板、控制器
      └── Entry/                           # Entry - 热更新入口
```
---
## 5. 开发规范
- 命名：PascalCase类/方法，camelCase私有字段
- 依赖：遵循分层，热更新通过桥接接口访问AOT
- 资源：Addressable异步加载，对象池复用
- 版本：Git + Git LFS

---

## 6. 当前进度
- [x] KCP通信基础（Scripts/KcpNet）
- [ ] 模块框架搭建（AOT/热更新结构）
- [ ] 3C系统
- [ ] 登录界面及流程
- [ ] 背包系统
- [ ] 武器系统
- [ ] 音效系统
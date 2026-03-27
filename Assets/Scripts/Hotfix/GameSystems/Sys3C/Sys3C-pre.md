请帮我为Unity MMO客户端项目生成3C系统（Character, Camera, Control）的完整代码实现。

## 项目背景
- Unity版本：2022 LTS
- 架构模式：AOT + 热更新分层（HybridCLR）
- 3C设计目标：
    - 角色：ECS/GameObject混合模式，动画状态机，本地移动+客户端预测
    - 相机：第三人称跟随（Cinemachine），支持旋转/缩放/碰撞
    - 控制：新输入系统，统一移动/交互/技能操作

## 代码组织要求
请严格按照以下目录结构和分层生成代码：

### AOT层（Assets/Scripts/AOT/）
1. **Core模块（Scripts/AOT/Core/）**
    - 事件系统：全局事件分发（GameEventDispatcher）
    - 输入系统：封装新输入系统，提供移动/交互/技能等统一输入事件
    - 对象池：GameObjectPool，用于特效/子弹等对象复用
    - 资源工具：Addressable异步加载封装（ResourceLoader）

2. **DataDefinition模块（Scripts/AOT/DataDefinition/）**
    - 定义3C相关接口：IPlayerController、ICameraHandler、IInputHandler
    - 定义事件常量：EventKey_Move、EventKey_Jump、EventKey_Attack等
    - 定义枚举：PlayerState、InputActionType

3. **Bridge模块（Scripts/AOT/Bridge/）**
    - 桥接类：Bridge_Player、Bridge_Camera、Bridge_Input
    - 供热更新层调用AOT层功能（如触发事件、加载资源、获取输入）

### 热更新层（Scripts/Hotfix/）
1. **GameSystems模块（Scripts/Hotfix/GameSystems/）**
    - 角色控制器：PlayerController（实现IPlayerController）
        - 移动逻辑（支持客户端预测）
        - 动画状态机（AnimatorController）
        - 跳跃/攻击等行为
    - 相机控制器：ThirdPersonCameraController（实现ICameraHandler）
        - Cinemachine FreeLook相机配置
        - 旋转/缩放/碰撞检测
    - 输入处理器：InputHandler（实现IInputHandler）
        - 监听新输入系统，转换为游戏逻辑事件

2. **Entry模块（Scripts/Hotfix/Entry/）**
    - 热更新入口：HotfixEntry
        - 初始化3C系统（创建Player、Camera、Input实例）
        - 注册桥接回调

## 技术细节要求
1. **角色控制**：
    - 使用CharacterController组件实现移动
    - 动画状态机控制Idle/Walk/Run/Jump/Attack
    - 移动输入驱动速度，支持加速/减速平滑过渡
    - 客户端预测：本地立即响应移动，后续服务端校验

2. **相机控制**：
    - 使用Cinemachine FreeLook相机，绑定玩家Transform
    - 支持鼠标拖拽旋转视角、滚轮缩放
    - 相机碰撞：动态调整相机距离避免穿墙

3. **输入系统**：
    - 使用Unity新输入系统（Input System Package）
    - 定义InputActionAsset：移动（Vector2）、跳跃（Button）、攻击（Button）、交互（Button）
    - 输入事件通过事件系统分发，供热更新层监听

4. **资源加载**：
    - 玩家模型、动画控制器通过Addressable异步加载
    - 使用ResourceLoader封装类，支持加载完成回调

5. **对象池**：
    - 特效、子弹等频繁创建销毁的对象使用对象池管理
    - 提供Spawn和Recycle接口

6. **事件系统**：
    - 全局单例EventDispatcher，支持注册/注销/派发
    - 热更新层通过Bridge访问事件系统

## 需要生成的文件清单
请按以下结构输出代码文件：

```plaintext
AOT/
├── Core/
│   ├── EventDispatcher.cs
│   ├── InputManager.cs
│   ├── GameObjectPool.cs
│   └── ResourceLoader.cs
├── DataDefinition/
│   ├── Interfaces/
│   │   ├── IPlayerController.cs
│   │   ├── ICameraHandler.cs
│   │   └── IInputHandler.cs
│   ├── Constants/
│   │   └── EventKeys.cs
│   └── Enums/
│       ├── PlayerState.cs
│       └── InputActionType.cs
└── Bridge/
    ├── Bridge_Player.cs
    ├── Bridge_Camera.cs
    └── Bridge_Input.cs

Hotfix/
├── GameSystems/
│   ├── PlayerController.cs
│   ├── ThirdPersonCameraController.cs
│   └── InputHandler.cs
└── Entry/
    └── HotfixEntry.cs
```
## 验收标准
所有代码符合PascalCase/camelCase命名规范

- 热更新层不直接依赖Unity Engine（通过桥接访问必要功能）

- 移动平滑，相机跟随无抖动

- 输入响应无延迟感

- 代码包含必要的注释和错误处理

- 支持Addressable异步加载玩家模型
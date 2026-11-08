# 怪物 AI 重构：Unity 专家复核

日期：2026-11-08。复核对象：[现状评审](../../Design/MonsterAiRefactorReview.md)。环境：Unity 2022.3.25f1、Built-in、GameSys/SyncCore/Cool.Protocol、移动平台、IL2CPP。

## 结论

**可进入 spec，需按下列边界修订。** 模式机 + 响应式行为树 + 独立 ActionRunner 适合当前项目；无需升级引擎、引入 DOTS 或替换 Animator。此结论是设计可行性评审，不是实现完成、性能达标或用户已批准完整设计。

复核由独立 Unity 专长审阅者完成，只读查看报告、模块说明及少数相关源文件，没有修改资源或重跑测试。

## 事实与表述纠偏

| 项目 | 复核判断 | spec 必须规定 |
|---|---|---|
| Slime/TurtleShell 半径 | 当前模型 bounds 算法与 JSON 0.5 的漂移已证实；0.858/0.832 不是已经确认的设计碰撞尺寸 | 固定姿态、根旋转、缩放后校准 CollisionProfile，导出稳定数据；客户端和服务端消费同一发布结果 |
| 普通受击后仍结算 | 已证实；用户本轮已确认普通怪前摇可打断，因此需修复 | 动作和未提交伤害同时取消；明确同 tick 顺序、冷却、霸体与后摇 |
| 残留 NavMeshAgent | enabled/update* 已确认，但不足以证明实际绑定导航或已发生帧内争写 | 纯联网视图排除自主导航；以 PlayMode 验证，而不把推断当实测 |
| 循环剪辑、AnyState、Write Defaults | 均不能仅凭单项设置判错 | 以联网表现合同校验；不得批改共享 FBX 导入器，改变冻结的单机行为 |
| AOI 攻击恢复 | 视图创建前丢事件、缺少当前动作状态的缺口成立 | 持久动作快照、实例和修订号、时间轴恢复 |
| awaitingConfirm | 没有期限是边界风险，尚无本轮弱网复现 | 新路径不依赖无限等待；保留旧路径仅供明确协商的兼容模式 |
| 同屏性能与跨端一致性 | 先前测试不足以证明端到端或设备达标 | 三客户端、弱网、Android 和服务端负载单列验收 |

`NpcBodyCollider.cs:63` 对“当前蒙皮包围盒”的注释需在实现阶段修正：SMR 默认 localBounds 可能包含导入动画范围，Renderer.bounds 是世界 AABB，受旋转与缩放影响。BodyRadius 的逻辑圆与客户端 BoxCollider 也不构成精确等价几何。

相关源码：

- `Server/Monster/MonsterModule.cs:290`：基于 HP 变化检测受击。
- `Server/Handler/CastHandler.cs:187`：待结算施法尚无动作取消校验。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs:402`：视图创建前的攻击事件丢弃。
- 同文件 `:734`：StripGameplay 保留 NavMeshAgent 等其它组件。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs:95`：状态快照消费；`:100`：确认等待。

## 进入设计时锁定的约束

1. 服务端独占决策、移动、命中和生命状态；BT 选行为，ActionRunner 持有持续动作；Animator 不反向决定逻辑结束。
2. 同一动作在同一 renderTick 下要对应同一技能、阶段和结果，不承诺不同网络条件下同时显示同一帧。
3. 恢复动画需明确层、完整状态路径 hash、进度映射、混合及 Trigger 清理，避免每帧 Play 重置。
4. 保留 NpcBody 层 10、本地玩家层 9、相机排除和客户端 BoxCollider；BodyRadius 与 NavRadius 分离。
5. 本地 AI 保留、冻结；Bon 完全排除。旧 2026-11-02 spec 的删除单机计划不自动成为本次要求。
6. 使用真实程序集边界：业务目前在 GameSys，不是每模块一个 asmdef；Editor 工具不能被运行时或服务端引用。
7. 新增动作字段不等于旧客户端具备新语义。混合版本必须明确协商或拒绝，不能静默进入同一新行为世界。
8. 新资源加载使用既有 Res 门面及引用计数；异步加载必须处理 AOI 离开、迟到完成、重连和池复用。

## 验证边界

上一轮基线是服务端 157 项与 Unity EditMode 22 项通过，另有 Hit 中仍出伤的隔离诊断。本次复核没有重跑这些测试；三客户端、弱网、Agent 帧内影响和 Android 性能仍未验收。

## 官方依据

- [Renderer.bounds：世界 AABB](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer-bounds.html)
- [Renderer.localBounds：蒙皮边界与动画范围](https://docs.unity3d.com/cn/2022.3/ScriptReference/Renderer-localBounds.html)
- [NavMeshAgent.updatePosition](https://docs.unity3d.com/cn/2022.3/ScriptReference/AI.NavMeshAgent-updatePosition.html)
- [Animator.Play：指定 normalizedTime](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Animator.Play.html)
- [AnimatorCullingMode](https://docs.unity3d.com/cn/2022.3/ScriptReference/AnimatorCullingMode.html)

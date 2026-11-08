# 怪物 AI 运行时重构 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development 或 executing-plans 逐任务实现此计划。步骤使用复选框跟踪；用户已指示实施，连续执行，不再停下来请求重复确认。

**目标：** 完成现有怪物 ActionV1、可恢复联网表现及战术走位，保护单机和玩家战斗。
**架构：** 引擎无关配置/协议在 SyncCore/Cool.Protocol，服务端持有动作和裁决，Unity 只消费时间轴与发布目录。现有工程仍使用 GameSys 业务程序集。
**技术栈：** Unity 2022.3.25f1、C#9/.NET Standard2.1、.NET8、UniTask、Addressables、protobuf-net3.2.56。
**规格：** [已复审 spec](../specs/2026-11-08-monster-ai-runtime-refactor-design.md)。

## 全局约束

- Bon 排除；单机冻结；PvP/玩家技能几何、协议既有 tag/值不变。
- 普通怪前摇可打断，Boss/指定技能配置霸体；取消要同时阻止未提交判定，已接受冷却不退。
- 一实体一主动作；第一Active tick首段未执行前仍可打断；空挥也推进阶段。
- 16次/25ms导航总预算；技能评分不能抢占持续动作。
- NpcBody层10/玩家层9不变；EntityRoot单位缩放，Profile用世界米。
- 新路径禁止handle.Task/同步阻塞；业务通过Res，异步完成有代次/引用配平。
- 只提交本任务文件。工作区有用户其它改动，不撤销、不顺带提交。
- 使用当前连接Unity的检出及codex/monster-ai-runtime-refactor分支；不复制用户插件、Library或无关改动到新检出。

## 审查重点（Review Focus）

1. 首Active tick玩家先命中取消，NPC先判定不回滚；同tick状态发布不篡改已入队DTO。
2. 同一持续动作在exit5/reentry8仍带EffectiveTick0，必须恢复而非按过期丢弃。
3. 暂停500ms/事件先于首快照/模型加载后离场，旧动作和旧模型不得复活。
4. 同key并发加载失败/取消/重试，不泄漏句柄或误释放另一个等待者。
5. 200 NPC战术思考不能产生2000q/s寻路洪峰；无预算不能误判不可达脱战。

## 文件及接口所有权

- 任务1：`AOT/Core/Resource/`及资源测试；任务2：Protocol、新增`SyncCore/Npc/`纯数据及测试。
- 任务3：`Server/Monster/`、`Server/Handler/CastHandler.cs`、`Server/Config/MonsterConfig.cs`、相关服务器测试；可以扩展CombatRules NPC配置入口，不改玩家表。
- 任务4：`Monster/Catalog/`、`Editor/Monster/`、生成目录/纯视图资产、Catalog测试；由主代理负责。
- 任务5：Server/Aoi、Server3C、LoginHandler、Program、EntityStore及该区域测试；与任务3以接口协作。
- 任务6：SyncLayer、客户端网络宿主/管理器/registry、动作轨迹/模型池/动画和Unity测试；由主代理负责。
- 任务7：集成与剩余测试、逐模块README、最终报告。

## 任务1：资源异步加载前置门禁

- [ ] 在`Assets/Scripts/Tests/Editor/ResourceAsyncLifecycleTests.cs`覆盖同key并发、失败重试、兼容/不兼容类型与引用配平，先运行看失败。
- [ ] 修改`ResourceManager.cs`/必要辅助类，保留Res API；UniTask原生等待，in-flight合并，失败释放，取消消费者不取消共享操作。
- [ ] 运行Unity EditMode资源测试与原有资源生命周期测试；确认发现数>0。
- [ ] 更新Resource README，提交本任务文件。

## 任务2：共享目录与ActionV1协议

接口约定：`Cool.Protocol.NpcActionSnapshot`拥有spec表中20个PascalCase属性，Kind/Phase/CancelReason用byte底层具名枚举；`Clone()`复制值。`NpcActionEvent.Action`、`EntitySnapshot.NpcAction/NpcActionHistory`、`CastBroadcast.NpcAction`。WorldSnapshot使用spec原名。`ProtocolFeatures.NpcActionTimelineV1`为uint常量1。

纯数据放`Cool.SyncCore`下`NpcCatalogDefinition`及Template/Skill/Collision/Ai/Presentation定义，均`[Serializable]`具名类和public PascalCase字段，供Unity序列化。`NpcCatalogValidator.Validate(catalog)`返回错误列表；`NpcCatalogHasher.Compute(catalog)`返回跨平台稳定SHA256，不依赖Unity/平台包。Skill定义包含SkillId/ShapeType/MaxRange/AreaRadius/Angle/CooldownTicks/WindupTicks/ActiveOffsets/RecoveryTicks/ArmorOverride/PresentationId；Template绑定技能优先级/距离/血量条件。请用小文件和明确默认值。

- [ ] 先加服务器`NpcActionProtocolTests`及`NpcCatalogDefinitionTests`，断言新tag回环、legacy缺省、hash排序/平台无关、非法时长拒绝。
- [ ] 追加消息、能力位和MessageCodec注册；保留既有tag/ProtocolVersion1。
- [ ] 实现纯数据/验证/hash及tick环绕/ActionSnapshot复制工具；C#9兼容。
- [ ] 跑相应服务器测试，更新Protocol/SyncCore README，提交本任务文件；将接口清单通知其它实现者。

## 任务3：服务端动作与战术

`MonsterState.ActionRunner`持有`MonsterActionRunner`，公开`Current`（冻结快照）、`CanStartSkill`、`TryBeginSkill(skillId,targetId,tick)`/等效提交入口、`OnDirectDamage(tick)`、`TryCommitStage(instanceId,tick)`、`Cancel(reason,tick)`及历史读取；接口可依现有实体结构合理细分，但必须在报告中写清给任务5消费。

- [ ] 添加Runner与Combat集成测试：普通前摇取消零伤害、Boss霸体继续、首段先后顺序、空挥推进/后摇、目标失效/死亡、取消不退冷却。
- [ ] 实现Runner并接通MonsterModule、BT、CombatResolver令牌；Legacy可保留，ActionV1显式模式，不能把玩家PendingCast套NPC规则。
- [ ] 增加Typed MovementResult、暂不可达排除、有限Search/Evade、DesiredSkill设置走位和统一预算调度，保持旧导航护栏。
- [ ] 添加低血DogKnight退让50005/受阻有限回退、预算非失败、goalRevision过期结果等行为测试。
- [ ] 运行Monster/Brain/SkillSelector/PathFollower/Combat相关测试；更新README并提交本任务文件。

## 任务4：Unity Catalog与发布资源

- [ ] 创建`Monster/Catalog/MonsterCatalog.cs`（SO持有共享Definition）及`NpcCatalogManifest`、Profile适配；`Editor/Monster/GameSys.EditorMonster.asmdef`/Exporter/Validator，EditorTests引用新程序集。
- [ ] 从当前四模板建立明确目录和稳定几何，生成独立纯联网视图/资源键，不改本地Prefab或共享FBX；标记模板2的视觉占位。
- [ ] 原子导出服务端JSON、共享NPC技能和客户端启动manifest；生成语义ContentHash及平台BuildHash/新鲜度摘要。
- [ ] 真实Catalog测试遍历资产/参数/层/碰撞，替换旧手写1/2球体断言；检查两次导出确定性。
- [ ] 用Unity预览固定姿态校准轮廓，保留几何实测记录；无设备证据不能伪称设备验收。

## 任务5：服务端能力握手和AOI批次

- [ ] 先测能力/hash拒绝、旧服务器/Legacy缺省、首入全量、注销先捕获exit、同tick多片不丢、短动作历史和出站冻结。
- [ ] 将ActionV1启动配置、发布Catalog加载与Runner接入Server3C/LoginHandler；登录要求位1和语义hash匹配。
- [ ] AOI保留每观察者动作基线/last-sent；统一tick采样锚点，40KiB分片，整批入队才提交游标；原ID不变。
- [ ] 保持DamageResult既有扩播；NPC动作事件只面向当前NPC观察者；更新相关README并提交。

## 任务6：客户端时间轴和异步视图

- [ ] 先测独立`NpcActionTrack`/`NpcSnapshotBatchAssembler`：EffectiveTick、未来状态不提前、同阶段修订、持续动作重入、短历史、分片/溢出/重连。
- [ ] 连接前Res加载manifest，发送能力/hash，按响应选择Legacy/ActionV1；新批次异常明确重连。
- [ ] Registry数据先于模型，按观察区间管理目标/碰撞；通过`INpcViewFactory`异步/池化模型，Profile建碰撞；回收清全部代次/订阅/Trigger。
- [ ] Animator按完整状态路径/阶段进度恢复，不每帧Play，不再以无限busy确认决定退出；NPC动作只有Track一个来源。
- [ ] 跑真实控制器/资源/Registry测试，单机/玩家路径回归；更新README并提交。

## 任务7：集成、宽范围审查与验收记录

- [ ] 运行服务器完整套件及Unity相关EditMode/PlayMode，构建C#9/IL2CPP相关新协议形状验证。
- [ ] 三客户端/延迟抖动/暂停/重入执行可自动化的日志对齐；核对actionId/phase/HP和资源回基线。
- [ ] 按AC-01..18逐项写pass/fail/not-verified及证据；Android参考设备/WebGL真包缺少实际执行环境时明确not-verified，不把代码测试当平台验收。
- [ ] 新审阅者覆盖整个本功能diff；修复承重发现，再运行受影响检查。
- [ ] 更新spec状态/实施报告/任务账本；只提交本功能文件，不合并、不推送、不发布。

## 覆盖与执行裁决

AC01/02→任务4；AC03→4/6；AC04/05→3；AC06→4/6；AC07→2/5/6；AC08/09/10/11→5/6/7；AC12/13→3；AC14→5/7；AC15/16→1/6；AC17→5/6；AC18→2/5/6/7。

Ruling: 用户“实施”已授权计划和后续实现，不再请求重复审批；保留当前Unity连接的检出，切换专用分支，避免复制用户未提交插件/场景改动。所有任务只管理各自文件。

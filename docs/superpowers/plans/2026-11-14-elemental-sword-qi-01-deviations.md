# 属性剑气 Plan 01 实施偏差记录

日期：2026-11-14
规格：[2026-11-14-elemental-sword-qi-design.md](../specs/2026-11-14-elemental-sword-qi-design.md)
计划：[2026-11-14-elemental-sword-qi-01-foundation.md](2026-11-14-elemental-sword-qi-01-foundation.md)

本文件记录计划示例与现有代码/规格冲突时的最小修正及原因。规格优先。

## 1. `CombatResolver` 目录注入方式

计划示例假定 `new CombatResolver(store)` 直接可用，但现有构造函数是
`CombatResolver(EntityStore, Random?)`，且大量既有测试按两参数调用。
实现改为第三可选参数 `ElementalCatalogDefinition? catalog = null`：
既有调用不受影响，生产由 `Server3C` 注入启动时加载的共享目录。
原因：避免改动既有 20+ 处测试调用，保持计划示例的兼容语义。

## 2. 客户端目录加载

规格要求双端读同一份 JSON。Plan 01 的服务端从
`Server/Config/elemental_catalog.json` 加载并校验；客户端使用
`ElementalCatalogDefaults.Create()`（与 JSON 由同一默认表生成）并在登录时
交换 SHA-256 指纹，不匹配则拒绝进入功能场景。
Unity EditMode 测试读取实际构建输入，逐项核对 JSON 默认表与
`SwordQi_Heavy.asset`。Addressables 目录加载留到后续计划。
原因：Plan 01 不引入新的 Addressables 资源组；指纹交换已能保证双端配置一致。

## 3. 服务端登录发放与测试接缝

`Server3C.RegisterEntity` 改为返回 `PlayerState`，登录处理器据此向
EntityStore 的权威实体发放武器；`Server3C` 增加仅在未启动网络时使用的
`OfflineSendSink`，用于从真实 `LoginHandler` / `EquipHandler` 路径观察下行消息。
原因：现有单元测试不启动 socket，而验收要求走真实 Handler 和协议输入。

## 4. 协议最小扩展

- `RespawnNotice` 追加 `LifeId`（tag 7），使客户端在复活后能携带正确生命代次；
  否则 `CastRequest.CasterLifeId` 在第一次复活后永久过期。
- `ProjectileLaunchBroadcast` 追加 `SkillId`（tag 13）与 `ServerTimestamp`（tag 14），
  用于晚到发射的表现查表与按服务器时钟补进度。

## 5. 旧客户端/旧测试兼容

`CastRequest.ClientCastSeq == 0` 视为旧请求：跳过幂等缓存；`CasterLifeId == 0`
跳过生命代次校验。新客户端始终发送非零序号与当前生命代次。
原因：既有服务端测试与旧客户端不填这些字段；强制校验会让全部旧用例拒绝。

## 6. 剑气元素表现

Plan 01 的 `ProjectileLaunchBroadcast` 携带并跟踪 `ElementId`，服务端与客户端
断言同一 `CastId` 的元素一致；按元素切换剑气颜色/拖尾的 VFX 资源留到 Plan 04
（路线图把元素表现放在 04）。原因：当前仓库只有一份 `SwordQi_Heavy` 预制体，
Plan 01 不新增美术资源。

## 7. 本地武器模型切换

`CharacterAttackHandler` 增加 `SetWeaponFactory` / `EquipWeapon(templateId, elementId)`
接缝；当前仓库没有按元素区分的武器预制体，因此工厂为空时只更新权威装备状态与
普攻参数，不替换手持模型。背包点击会走真实请求/确认链路。
原因：避免在 Plan 01 伪造不存在的武器资源；可视模型资源随 04 补充。

## 8. 本地攻速缩放方式

`SkillStateMachine` 增加 `TimeScale`，只缩放 `_elapsedTime`（基本攻击 10001/10002
是 Combo 路径）。蓄力/旋转/引导路径保持原计时，因为攻速首版只作用于普攻。
`SkillCoordinator` 同时把冷却按同一倍率缩小（`Cooldown / attackSpeed`），
`CharacterAttackHandler`/`RemoteEntityAnimator` 写 Animator 的 `AttackSpeed` 参数。
原因：改全局 `Animator.speed` 或重写所有状态计时会越出 Plan 01 范围，且规格禁止
全局 Animator 速度。

## 10. 服务端场景标识

Plan 01 用登录 `ZoneId`（服务端房间键）作为 `CombatSceneId`。`PlayerState.ZoneId`
默认 `"Global"`，客户端登录固定发 `"Global"`；怪物实体沿用该默认值。跨场景隔离
测试通过直接设置两个玩家的 `ZoneId` 验证。怪物按地图实例绑定场景留到 Plan 02。
原因：现有服务端没有独立的 `CombatSceneId` 字段，登录房间键是最小可用真值。

## 11. 剑气视觉起点

服务端 `ProjectileLaunchBroadcast` 的 XZ 起点与判定同源（施法者脚底/朝向），
客户端不再叠加 `SwordQiConfig.SpawnOffset` 的水平偏移，避免表现与判定错位；
高度偏移也不叠加，首版剑气从权威原点飞行。离线路径仍使用资产偏移。
原因：规格要求“判定与表现使用同一水平起点和轴线”。

## 12. 提交粒度

任务 3 与任务 4 的 `Server3C` 接线在同一文件（`CombatResolver` 构造注入目录），
拆成两个可编译提交会引入临时回退；因此合并为
`feat(server): add authoritative equipment, cast ids and weapon cast snapshots`。
其余任务按计划单独提交。

## 13. PlayMode 双客户端

真实双客户端通过 PlayMode `ElementalTwoClientTests` 连接本机真实服务端
（`127.0.0.1:8123`）完成登录、权威换装、施法与同一 `CastId` 发射校验。
需要手动启动服务端的步骤写在测试注释与最终报告中。

## 9. Unity 测试环境

worktree 首次导入时 Package Manager 需要访问 GitHub/gitcode 拉取 UniTask 与
NuGetForUnity。构建机网络不可达时，测试使用 `Packages/` 内嵌的已缓存包副本，
并在本地临时移除 manifest 中两个 git URL；该临时改动不提交，测试后还原。

Unity 2022.3.25f1 + Test Framework 1.1.33 的 batchmode 实测：
`-runTests ... -quit` 会在测试启动前退出，不产生结果文件；必须**去掉 `-quit`**，
让 Test Framework 在跑完后自行退出。另外本项目自定义测试 runner 的
`--filter` 不支持 `|` 交替（返回 0 个测试），服务端过滤测试需逐类运行。


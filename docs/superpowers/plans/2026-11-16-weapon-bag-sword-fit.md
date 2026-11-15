# 简易背包换剑与持剑适配实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标** 完成男女角色的八组元素剑适配，验证简易背包从真实登录到装备确认和持剑显示的闭环。

**架构** 基于 cd478cb8 已有背包和模型接线。服务器摘要驱动客户端装备显示，角色预制体引用项目自有的校准变体，表现变更沿用现有挂点。

**技术栈** Unity 2022.3.25f1、C#、UGUI、Protobuf、KCP、Unity Test Framework。

**规格** [简易背包换剑与持剑适配](../specs/2026-11-16-weapon-bag-sword-fit-design.md)

## 全局约束

- 在元素剑气 worktree 中执行，每项先确认 RED，再确认 GREEN，最后分别提交。已通过的既有行为保留为回归断言。
- 先读 AGENTS.md、技术偏好、Bag、Sys3C、SyncLayer README。Unity 使用此 worktree 的项目路径，批处理运行测试时省略 -quit，以结果 XML 为证。
- 本期覆盖 DemoDay 联机、本地角色、默认剑及冰火雷风。校准目标为对应原配剑握把中心到剑尖长度的 90%–110%。所有模型变换属于表现层。

## 文件与职责

| 文件或目录 | 职责 |
|---|---|
| Assets/Prefabs/Character/WeaponViews/ | 八个项目自有校准变体，命名 MaleIceSword.prefab 至 FemaleWindSword.prefab，按角色与元素命名 |
| Assets/Prefabs/Character/FemalePlayer.prefab、Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab | 绑定各自四把校准剑与现有挂点 |
| Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs | 应用本地持剑外观，安全回退 |
| Assets/Scripts/Hotfix/GameSystems/Bag/UI/WeaponBagView.cs | 五行列表、已装备标记、反馈 |
| Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs、Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs | 消费装备摘要、重建角色与清理会话 |
| Assets/Scripts/Tests/Editor/Sys3C/WeaponViewFitTests.cs、Assets/Scripts/Tests/Editor/Bag/WeaponBagViewTests.cs | 资产和交互回归 |
| Assets/Scripts/Tests/PlayMode/ElementalTwoClientTests.cs | 联机消息回归 |

## 测试命令

每个 Unity 类单独运行。[Unity.exe] -batchmode -nographics -projectPath [当前 worktree 的绝对路径] -runTests -testPlatform [EditMode 或 PlayMode] -testFilter [类名] -testResults [独立 XML 路径] -logFile [独立日志路径]。执行 RED 和 GREEN 时分别留档。

## 审查重点

- 模型枢轴和尺寸不同。男女分别测量并检查全部八组。
- 配置缺失或换回默认剑。当前可见剑只有一把，权威装备值有效。
- 旧确认或拒绝。旧摘要不覆盖新版本，拒绝显示原因并使用权威摘要。
- 角色重建与断线。当前外观正确重建，旧面板与旧会话状态清理。
- 批处理退出成功但无 XML。只有 XML 中发现并运行预期用例才计为通过。

### 任务 1：校准 Sword View 数据与回退

- [x] 跑既有 BagWeaponInventoryTests（批处理执行，未打开 DemoDay；2/2 通过，见 `task1-bag-baseline.xml`）。
- [x] 新建 WeaponViewFitTests。断言两个角色的四个引用均指向对应 M/W 校准变体，缩放为有限正数且三轴相等。断言冰剑已显示时应用缺失的火剑配置返回 false、原剑可见、旧冰剑立即隐藏。断言随后切回默认剑成功且无重复模型。
- [x] 单独运行 WeaponViewFitTests，确认 RED：角色仍引用供应商资源、缺失模型未回退（4 failed / 1 passed）。
- [x] 创建 Male/Female × Ice/Fire/Thunder/Wind 八个校准变体。每个变体的根用挂点局部恒等变换，子对象按规格先缩放、再旋转、最后平移。保留供应商源文件及 GUID。校准写在项目自有变体内的模型子对象，root 保持挂点局部恒等。为每个角色换成对应变体引用。实测：男统一缩放 0.81511 + z=0.007，女 0.85685；长度比均为 1.000。
- [x] 更新 Sys3CEntry.ApplySwordView(int elementId)。元素 0 或缺失资源时恢复原配剑，每次交换先隐藏旧视觉。签名调整为 bool ApplySwordView(int elementId)，成功应用或重复应用返回 true，回退到原配返回 false。
- [x] 重跑 WeaponViewFitTests 全绿（5/5，后续补真实角色预制体循环 6/6）。八组测量保存到 `production/verify/weapon-bag-sword-fit/measurements.csv`。
- [x] 更新 Sys3C README 并提交（任务 1 提交）。
- [ ] 保存八组待机、跑动、轻击、重击、格挡以及默认剑回切的截图与动作画面（待 Unity 交互预览）。

### 任务 2：简易列表与权威显示对账

- [x] 新建 WeaponBagViewTests（19 个用例）。覆盖登录顺序、点击非当前项只发一次请求、确认前保留标记、过期确认无回滚、拒绝恢复并显示原因、缺配模型提示、角色重建重放与断线清理。覆盖连点只发一次、超时后可重试、迟到回包不解锁新请求，以及同帧结果只刷新一次。另加确认在技能中到达的用例，断言列表当帧更新，动作结束时只应用最新外观。
- [ ] 单独运行 WeaponBagViewTests，确认 RED。实际未单独留 RED（控制器为新增类型，旧视图没有反馈/生命周期状态）；GREEN 19/19 见 `task2-weapon-bag-green.xml`。
- [x] 复用现有 BagManager.RequestEquip 与 ClientEquipmentState 版本合并。宿主在确认或基线已应用时同步标记和外观。收到拒绝时显示原因。CombatNetworkBridge.RequestEquip(long weaponInstanceId) 返回发送的 ClientEquipSeq，0 表示未发送。用 OnEquipResult 关联在途请求，刷新统一读取 TryApply 后的 Equipment。避免 OnEquipmentChanged 和 OnEquipResult 同时刷新同一条结果。把同步状态保留在连接生命周期，角色重建重放摘要，断线清理面板、请求及提示。维持现有武器列表与点击入口。新增 `WeaponBagController` 承载会话状态机。
- [x] 分别运行 WeaponBagViewTests 19/19、ClientEquipmentStateTests 4/4、BagWeaponInventoryTests 2/2，并补跑 CombatNetworkBridgeTests 29/29。更新 Bag 和 SyncLayer README 并提交（任务 2 提交）。

### 任务 3：跨端验收与分支审查

- [x] 运行 bash tools/test-server-inprocess.sh 并记录通过数（512/512）。运行 dotnet build Server/Server3C.csproj 构建服务端（0 errors）。证据：`task3-server-tests.log`、`task3-server-build.log`。
- [x] 启动 dotnet Server/bin/Debug/net8.0/Server3C.dll 8123，运行 PlayMode ElementalTwoClientTests，XML 有通过的真实 KCP 双客户端测试（1/1）。证据：`task3-elemental-two-client.xml`、`task3-server.log`。
- [ ] 使用工作区的 Unity 对 DemoDay 做实际交互冒烟。男女角色分别登录、点击四把剑、切回默认剑、连续换剑并重连。对照任务 1 记录检查持剑、拖尾和背包标记。保存 XML、日志和操作记录。使用当前分支的实际发布候选配置补一次进入 DemoDay 的构建冒烟，确保剑变体、依赖和中文字体随包可用。**阻塞**：MCP 连接的 Unity 编辑器打开的是主仓库 `E:/CodeForJob/Cool`，本 worktree 未打开；出包冒烟未执行。自动证据已留档，本步骤保持未勾选。
- [x] 对照规格审查完整分支，检查资源 GUID、默认回切、版本过滤、角色重建及范围。GUID 重复数为 0；默认回切、缺失回退、版本合并、重建摘要复用均有测试；修复了重建后未强制重放外观、登录前提前创建面板、遗漏的 HovlStudio 根 GUID。补跑受影响用例（`task3-weapon-view-prefab-cycle.xml` 6/6、`task3-combat-bridge-rebuild.xml` 30/30）。

## 执行记录（2026-11-16）

- 任务 1 提交：`feat: calibrate weapon bag sword views (task 1)`。
- 任务 2 提交：`feat: reconcile weapon bag equipment state (task 2)`。
- 证据目录：`production/verify/weapon-bag-sword-fit/`
  - `task1-bag-baseline.xml` 2/2；`task1-weapon-view-red.xml` 4 failed / 1 passed；
    `task1-weapon-view-green.xml` 5/5；`task3-weapon-view-prefab-cycle.xml` 6/6。
  - `task2-weapon-bag-green.xml` 19/19；`task2-equipment-state.xml` 4/4；
    `task2-bag-inventory.xml` 2/2；`task2-combat-bridge.xml` 29/29；
    `task3-combat-bridge-rebuild.xml` 30/30。
  - `task3-elemental-two-client.xml` 1/1；`task3-server-tests.log` 512/512；
    `task3-server-build.log` 0 errors；`task3-server.log`；`measurements.csv`。
- 偏差：
  - 新增 `Bag/UI/WeaponBagController.cs`（计划文件表未列）承载在途序号、5 秒超时、反馈与延迟外观，
    宿主保持薄接线以便 EditMode 测试。
  - 供应商目录 `Assets/HovlStudio` 与旧 `Assets/Hovl Studio` 存在重复 GUID；Unity 为新目录重分配 GUID，
    旧目录保留。当前分支 GUID 重复数为 0。
  - 交互 DemoDay 冒烟、动作截图与出包冒烟未执行（环境阻塞，见任务 3 第 3 条）。

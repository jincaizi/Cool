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
| Assets/Prefabs/Character/WeaponViews/ | 八个项目自有校准变体 |
| Assets/Prefabs/Character/FemalePlayer.prefab、Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab | 绑定各自四把校准剑与现有挂点 |
| Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs | 应用本地持剑外观，安全回退 |
| Assets/Scripts/Hotfix/GameSystems/Bag/UI/WeaponBagView.cs | 五行列表、已装备标记、反馈 |
| Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs | 消费装备摘要、重建角色与清理会话 |
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

- [ ] 打开 worktree 的 DemoDay，先跑既有 BagWeaponInventoryTests。若包解析失败，记录日志并恢复编辑器依赖后继续。
- [ ] 新建 WeaponViewFitTests。断言两个角色的四个引用均指向对应 M/W 校准变体，缩放为有限正数且三轴相等。断言应用缺失模型时恢复原配剑。
- [ ] 单独运行 WeaponViewFitTests，确认当前角色引用原资源且缺失模型没有回退。
- [ ] 创建 Male/Female × Ice/Fire/Thunder/Wind 八个校准变体。每个变体的根用挂点局部恒等变换，子对象按规格先缩放、再旋转、最后平移。保留源资产 GUID 与变换。为每个角色换成对应变体引用。
- [ ] 更新 Sys3CEntry.ApplySwordView(int elementId)。元素 0 或缺失资源时恢复原配剑，每次交换先隐藏旧视觉。签名调整为 bool ApplySwordView(int elementId)，成功应用或重复应用返回 true，回退到原配返回 false。
- [ ] 重跑 WeaponViewFitTests 并确认全绿。保存八组测量与截图到本 worktree 的 production/verify/weapon-bag-sword-fit/。每组验证待机、跑动、轻击、重击、格挡以及默认剑回切。更新 Sys3C README 并提交。

### 任务 2：简易列表与权威显示对账

- [ ] 新建 WeaponBagViewTests。覆盖登录顺序、点击非当前项只发一次请求、确认前保留标记、过期确认无回滚、拒绝恢复并显示原因、缺配模型提示、角色重建重放与断线清理。
- [ ] 单独运行 WeaponBagViewTests，确认尚未接通的反馈或生命周期断言失败。
- [ ] 复用现有 BagManager.RequestEquip 与 ClientEquipmentState 版本合并。宿主在确认或基线已应用时同步标记和外观。收到拒绝时显示原因。角色重建重放摘要，断线清理面板及提示。维持现有武器列表与点击入口。
- [ ] 分别运行 WeaponBagViewTests、ClientEquipmentStateTests 和 BagWeaponInventoryTests 并确认全绿。更新 Bag 和 SyncLayer README 并提交。

### 任务 3：跨端验收与分支审查

- [ ] 运行 bash tools/test-server-inprocess.sh 并记录通过数。运行 dotnet build Server/Server3C.csproj 构建服务端。
- [ ] 启动 dotnet Server/bin/Debug/net8.0/Server3C.dll 8123，运行 PlayMode ElementalTwoClientTests，检查 XML 有通过的真实 KCP 双客户端测试。
- [ ] 使用工作区的 Unity 对 DemoDay 做实际交互冒烟。男女角色分别登录、点击四把剑、切回默认剑、连续换剑并重连。对照任务 1 记录检查持剑、拖尾和背包标记。保存 XML、日志和操作记录。
- [ ] 对照规格审查完整分支，检查资源 GUID、默认回切、版本过滤、角色重建及范围。修复后重跑受影响用例。验收通过后勾选对应步骤，提交验收记录。

# P0/P1 质量改进实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 `subagent-driven-development`（推荐）或 `executing-plans` 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 清零当前审计中的 P0 风险，并完成移动端公开联机测试前必须具备的 P1 工程基线。

**架构：** 服务端收敛为“有界准入 -> 鉴权 Session -> WorldRouter -> 单线程 WorldInstance 邮箱”；客户端收敛为“网络回调 -> 有界 ingress mailbox -> 主线程业务分发”；资源收敛为“Addressables 单飞加载 -> 显式 ResourceLease”。P0 先以单 `Global` 世界收口风险，P1 再开放真正隔离的多 WorldInstance。

**技术栈：** Unity 2022.3.25f1、C#、IL2CPP、Addressables、UniTask、KCP/WebSocket、Protobuf、.NET 8、xUnit、NUnit、GitHub Actions/自托管 Unity runner。

**规格：** [2026-11-05-p0-p1-quality-improvement-design.md](../specs/2026-11-05-p0-p1-quality-improvement-design.md)

## 全局约束

- **阶段 0 先行**：Spec、计划与 ADR 修订完成且阶段 0 退出清单全绿前，不改运行时代码。
- 不安装 HybridCLR；`Hotfix.*` 只是普通 asmdef。
- 服务端世界状态只有单线程拥有；IO 线程只验证、解码并向有界邮箱投递。
- 客户端 Unity API 只在主线程调用；网络回调不得读取 `Time.*` 或触碰 Unity 对象。
- `SyncCore` 零 `UnityEngine` 依赖，规则改动必须同时通过 Unity 与服务端测试。
- 协议只追加字段；不复用 ProtoMember tag；破坏语义时提升 `ProtocolVersion`。
- **平台传输矩阵**：Android/iOS 主传输为 KCP + Protobuf，公网候选包必须叠加经 ADR 批准的标准安全层；WebGL 主传输为 WebSocket，公网为 WSS。每个平台和构建配置只允许一个主传输；Editor/Windows 测试按 lane 显式选择，Native KCP lane 与 WebSocket/WebGL lane 分开报告。不得把任一平台的选择外推为所有平台的默认值。
- 每个行为改动同步相应模块 README。
- 非内部 MonoBehaviour 禁止新增 `GetComponent/FindObject*`。
- 当前工作树存在大量无关改动；每次只暂存本任务明确列出的路径，禁止 `git add .` / `git add -A`。
- **dirty worktree 只产生 `DIAGNOSTIC_ONLY`**：`--infra` / `--baseline` 在 dirty worktree 上只能输出诊断结果并保存 patch hash；`--p0`、`--p1` 与 Release Readiness 要求 clean worktree，dirty 时不得产生 PASS。
- **任务级证据使用 run-id**：每次执行生成唯一 `run-id = <UTC timestamp>-<short-git-sha>`，XML、日志与构建产物写入 `production/quality/artifacts/<run-id>/`；门禁报告写入 `production/quality/<gate>-<run-id>.md`。报告引用同一 run-id；旧产物与历史日志不得作为本轮证据。
- 每个任务先红灯、再最小实现、再目标回归、再提交；不得把多个任务揉成一次大提交。
- P0 未通过前冻结新增联网玩法；P1 未通过前不标记“公开测试/商店发布准备”。

## 审查重点（Review Focus）

1. `uint` sequence 在回绕、重复和倒序时必须只接受严格更新值；任务 7 的双端测试锁定该行为。
2. 可靠命令队列满时不得静默丢失或覆盖；任务 9、10、12 分别覆盖服务端 loop、传输和客户端邮箱。
3. Addressables 在并发、取消、异常和重复场景加载时不得泄漏 handle；任务 21 覆盖全部失败路径。
4. 登录重传、换身份和断线竞态不得产生幽灵实体或重复清理；任务 6 覆盖 session/account/character 映射不变量。
5. 世界切换失败必须恢复源世界，且任何跨实例 AOI/伤害/仇恨/ORCA 结果均为 0；任务 15、16 覆盖该不变量。

---

## 交付波次

| 波次 | 任务 | 退出条件 | 门禁产物 |
|---|---|---|---|
| 阶段 0 | 文档修订 | 复核指出的架构矛盾全部消除；任务文件清单足以实施 | 修订后的 Spec 与计划 |
| Gate 0 | 1 | runner、测试发现、server profile、XML 与失败传播可信 | `production/quality/gate-0-<run-id>.md` |
| P0-A | 2–4 | Bag、伤害上下文、EventBus/技能事务全绿 | 波次报告 |
| P0-B | 5–8 | 单世界、幂等登录、移动、格挡精力全绿 | 波次报告 |
| P0-C | 9–13 | 有界队列、准入背压、KCP、客户端邮箱全绿 | 波次报告 |
| P0 Gate | 14 | 原 Spec 10.1 + 复核 7.1 全部满足 | `production/quality/p0-gate-<run-id>.md` |
| P1-A | 15–18 | WorldInstance、AOI、ORCA、导航规模门禁 | 波次报告 |
| P1-B1 | 19–23 | 伤害管线、Sys3C 注入、资源、Addressables 与 UI 生命周期收敛 | 波次报告 |
| P1-B2 | 24–28 | 性能、局部 HitStop、VFX 与 Monster 池收敛 | 波次报告 |
| P1-C1 | 29–31 | 移动构建、架构守卫、CI 与 Coverage | 波次报告 |
| P1-C2 | 32 | 可观测性、WSS 部署、移动 KCP 公网安全与优雅停机 | 波次报告 |
| P1 Engineering Gate | 33 | 原 Spec 10.2 的工程项 + 复核 7.2 的自动化项满足 | `production/quality/p1-engineering-gate-<run-id>.md` |
| Mobile Release Readiness | 33 | 当前目标平台的设备运行、性能和生命周期证据全部满足 | `production/quality/mobile-release-gate-<run-id>.md` |
| WebGL Public Deployment Readiness | 33 | WebGL 被列为 blocking platform 时，公开域名/WSS/证书/反向代理与浏览器往返证据全部满足 | `production/quality/webgl-public-gate-<run-id>.md` |

波次之间严格串行，P0-A 与 P0-B 不得跨波次并行；波次内部仅当文件所有权与依赖互不重叠时才允许并行，且每个并行任务必须使用独立 worktree、独立 Unity `Library`、独立端口与独立 artifact 目录。合并回集成分支后必须重新执行该波次完整 Gate。

## 文件结构总览

| 工作域 | 主要新增/修改位置 | 职责 |
|---|---|---|
| 质量入口 | `tools/quality-gate.sh`、`tools/unity-tests.sh`、`Assets/Scripts/Editor/Quality/` | 非零测试、构建目标、聚合结果 |
| 客户端正确性 | `Bag/`、`Skills/`、对应 Editor tests | 数据保真、不可变配置、订阅与事务 |
| 协议/权威 | `AOT/Protocol/`、`SyncCore/`、`Server/Auth/`、`Server/Combat/` | 登录、移动、防御、双端规则 |
| 服务端容量 | `Server/Ingress/`、`Server/Transport/`、`Server/GameLoop.cs` | 有界队列、准入、背压、KCP 所有权 |
| 客户端网络 | `SyncLayer/ClientIngressMailbox.cs`、`ClientNetworkManager.cs` | 主线程分发、有限预算、可取消登录 |
| 多世界/空间 | `Server/World/`、`Server/Aoi/`、`Server/Movement/`、`Server/Navigation/` | 世界隔离、空间索引和异步路径 |
| 资源/UI | `AOT/Core/Resource/`、`UI/`、Addressables settings | lease、单飞加载、面板生命周期 |
| 发布治理 | `Assets/Scripts/Editor/MobileBuildScript.cs`、`.github/workflows/` | Android/iOS、CI、架构与覆盖率门禁 |

---

### 任务 1：建立可信质量门禁（Gate 0）

**优先级：** P0 / 所有任务前置

阶段 0 完成后，第一波只做本任务。

**文件：**
- 新建：`Assets/Scripts/Editor/Quality/GameSys.EditorQuality.asmdef` 及 `.meta`
- 新建：`Assets/Scripts/Editor/Quality/QualityGate.cs` 及 `.meta`
- 新建：`Assets/Scripts/Editor/Quality/AssemblyInfo.cs`（`InternalsVisibleTo`）及 `.meta`
- 修改：`Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`（引用 `GameSys.EditorQuality`）
- 新建：`Assets/Scripts/Tests/Editor/Quality/QualityGateTests.cs`
- 新建：`Assets/Scripts/Tests/Editor/Architecture/PlayModeAssemblyContractTests.cs`
- 移动并保留 `.meta`：`Assets/Scripts/Tests/PlayMode/KcpNet.PlayModeTests.asmdef` → `Assets/Scripts/Tests/PlayMode/GameSys.PlayModeTests.asmdef`；程序集名改为 `GameSys.PlayModeTests`，移除 `KcpNet` 引用与 `!UNITY_WEBGL`，保留纯 Unity 通用测试
- 新建：`Assets/Scripts/Tests/PlayMode/NativeNetwork/KcpNet.NativePlayModeTests.asmdef` 及 `.meta`；保留 `KcpNet` 引用与 `!UNITY_WEBGL`
- 新建：`Assets/Scripts/Tests/PlayMode/NativeNetwork/TestServerEndpoint.cs` 及 `.meta`
- 新建：`Assets/Scripts/Tests/PlayMode/NativeNetwork/NativeServerProfileSentinelTests.cs` 及 `.meta`
- 移动并保留各自 `.meta` 到 `Assets/Scripts/Tests/PlayMode/NativeNetwork/`：`AdapterConnectTests.cs`、`AoiWorldTests.cs`、`BlockMitigationE2ETests.cs`、`ClientRespawnWaitTests.cs`、`CombatSyncTests.cs`、`EchoSmokeTests.cs`、`HitJudgmentAlignmentTests.cs`、`LoginEnterWorldTests.cs`、`MonsterWorldTests.cs`、`MovementSyncTests.cs`、`ThreeClientCombatConsistencyTests.cs`、`TwoClientWorldTests.cs`
- 修改上述 Native 测试：移除硬编码 `127.0.0.1:8000`，改用 `TestServerEndpoint` 读取 `COOL_TEST_SERVER_HOST` / `COOL_TEST_SERVER_PORT`，并按 `native-clean`、`native-bots`、`native-monsters` 标记 profile
- 新建：`tools/quality-gate.ps1`（Windows 权威 runner）
- 新建：`tools/tests/quality-gate-selftest.ps1`（不依赖外部服务的 runner 负向自测）
- 新建：`tools/quality-gate.sh`（调用 PowerShell 权威 runner 的薄包装）
- 修改：`tools/unity-tests.sh`（薄包装）
- 新建：`tools/unity-tests-mcp.sh`（便利入口）
- 修改/重新生成：`production/quality/test-manifest.json`、`production/quality/gate-exceptions.json`（阶段 0 已建立基线，本任务迁移测试后重新生成，不重复创建）
- 修改：`docs/agent-token-hygiene.md`

所有位于 `Assets/` 下的新建或移动文件都必须在提交中包含对应 `.meta`；移动使用 Unity AssetDatabase 或等价的“文件 + `.meta`”原子移动，禁止重新生成 GUID。

- [ ] **步骤 1：编写失败的测试与门禁断言**

`QualityGateTests` 固定以下纯验证断言（不切 BuildTarget，不解析当前 NUnit run 的最终结果）：

```csharp
[Test] public void test_quality_gate_rejects_default_company_and_application_id();
[Test] public void test_quality_gate_playmode_target_is_standalone_windows64();
[Test] public void test_generic_playmode_assembly_has_no_webgl_exclusion();
[Test] public void test_native_network_tests_are_isolated_and_exclude_webgl();
```

`PlayModeAssemblyContractTests` 断言 `GameSys.PlayModeTests.asmdef` 不含 `KcpNet` 引用与 `!UNITY_WEBGL`，`KcpNet.NativePlayModeTests.asmdef` 保留两者，且 12 个 Native 测试文件只位于 `NativeNetwork/`。

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode QualityGateTests`
预期：FAIL，缺少 `QualityGate`。

- [ ] **步骤 3：实现 Gate 0 基础设施**

1. `QualityGate.cs` 只做 Editor 内 preflight：验证 active target、包名、asmdef 契约与配置，并提供可被 EditMode 直接调用的纯验证器；不调用 `SwitchActiveBuildTarget`，不判断当前 NUnit run 的最终发现数或失败数。
2. `quality-gate.ps1` 在 Unity 退出后解析 XML，验证 discovered、failed、skipped、manifest、进程退出码、timeout、产物新鲜度与 hash。
3. 原生 runner 启动独立 Unity 进程并传 `-buildTarget StandaloneWindows64`，Editor 内验证 `BuildTarget.StandaloneWindows64`；测试进程内不切换 BuildTarget。EditMode、通用 PlayMode 与每个 Native server profile 分别输出独立 XML 与日志。同一 project 已被 Unity 打开时 fail-fast，并提示使用独立 worktree。
4. 服务器先执行 `dotnet build Server/Server3C.csproj -c Debug`，再以 `dotnet Server/bin/Debug/net8.0/Server3C.dll <port> <profile args>` 启动。runner 记录 PID，使用动态 TCP/UDP 端口，先同时观察现有日志 `Server3C started on port ... with KCP protocol` 与 `WebSocket listener started on ...`，再执行对应传输 probe；timeout 或失败时只终止本次进程树。若改为统一 `SERVER_READY` marker，必须同时把 `Server/Program.cs` 与对应服务端测试加入本任务。
5. Native profile 固定为：`native-clean` 不带 bots/monsters，运行除特殊 profile 外的 Native 测试；`native-bots` 运行 `AoiWorldTests`，使用 `--bots 100 --bots-radius 500`；`native-monsters` 运行 `MonsterWorldTests` 与 `HitJudgmentAlignmentTests`，使用 `--monsters 4` 及测试要求的地图参数。新增导航专用测试时再启用 `native-navigation`，不得建立空 profile。profile 只描述服务器配置；runner 根据 `test-manifest.json` 按 fixture/cohort 多次调用 Unity `-testFilter`；默认每个 fixture 使用全新服务器进程，只有被显式证明状态互不影响的 fixture 才能组成共享进程 cohort，禁止跨 profile 或未声明 cohort 共享世界状态。
6. `test-manifest.json` 保存每个 assembly/profile 的预期测试全名；测试新增、删除或迁移必须显式更新 manifest；未解释的测试数下降、未知 skip/ignore/inconclusive 均失败；`discovered == 0` 直接失败。
7. WebGL 使用独立 `-buildTarget WebGL` 进程。Gate 0 区分“通用测试程序集 WebGL compile contract”“WebGL Player Build”和“WebGL runtime smoke”，三者不得互相替代。移除 `GameSys.PlayModeTests.asmdef` 的 `KcpNet` 直接引用只隔离测试程序集，不代表生产传输已隔离；`GameSys.asmdef` 仍引用 `KcpNet` 与 `WebSocketNet`，由任务 13 用 platform factory、预处理编译守卫、PluginImporter/产物检查和 WebGL compile contract 证明 KCP 玩家路径不可达，任务 30 只维护这组已通过 P0 Gate 的守卫。
8. `quality-gate-selftest.ps1` 用 stub 进程与 fixture XML 证明以下情况必定非零并保留证据：server ready 超时、Unity 非零退出、XML 缺失/损坏、manifest 数量下降、过期 exception、stale artifact、teardown 超时与残留子进程。
9. `--infra` 与 `--baseline` 在 dirty worktree 上只能输出 `DIAGNOSTIC_ONLY` 并保存 patch hash；`--p0`、`--p1` 与 Mobile Release Readiness 要求 clean worktree，dirty 时不得产生 PASS。
10. Gate 0 运行 `Assets/Scripts/Tests/Editor/Network/ProtocolRoundtripTests.cs`、`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs` 与服务端协议测试；缺跨端一致性断言时在本任务补一个双端 roundtrip。`quality-gate.ps1` 是 Windows 权威入口；Linux 默认只做内容 hash/静态检查，若要在 Linux 跑 Unity Gate 必须显式提供 `pwsh` 或 POSIX runner。

- [ ] **步骤 4：验证**

运行：

```bash
bash tools/quality-gate.sh --infra
pwsh -File tools/tests/quality-gate-selftest.ps1
bash tools/quality-gate.sh --baseline
```

验收：
- clean worktree 执行 `bash tools/quality-gate.sh --infra`，每个 assembly/profile 至少运行一个确定性 sentinel，退出码 0；这只证明 runner、发现、XML、服务器编排与失败传播可信。
- `quality-gate-selftest.ps1` 全部通过，确认负向场景不会假绿。
- 随后 `bash tools/quality-gate.sh --baseline` 运行全量测试并生成基线报告；产品测试可以在此阶段失败，但每个失败必须分类并映射到任务 2–13；runner、发现、profile、teardown 或证据失败仍阻塞任务 2。
- P0 Gate 执行 `bash tools/quality-gate.sh --p0` 时所有 blocking tests 必须全绿，不能沿用 Gate 0 baseline 的失败。
- EditMode、通用 PlayMode 与每个 Native profile 的 XML 均可解析且与 test manifest 一致；仅 `discovered > 0` 不足以通过。
- 每个 server profile 结束后无残留服务端进程；再次执行结果一致。
- dirty worktree 运行只能生成 `DIAGNOSTIC_ONLY`；clean worktree 的正式 Gate 报告记录 release scope 与 manifest/exception 文件 hash。
- WebGL compile contract 与 Player Build 使用独立结果；未执行 runtime smoke 时只能报告 `WebGL compile/build verified`，不能报告 runtime verified。
- 不依赖 MCP `tests-run` 作为证据。

- [ ] **步骤 5：Commit**

```bash
git add \
  Assets/Scripts/Editor/Quality \
  Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef* \
  Assets/Scripts/Tests/Editor/Quality \
  Assets/Scripts/Tests/Editor/Architecture/PlayModeAssemblyContractTests.cs* \
  Assets/Scripts/Tests/PlayMode \
  tools/quality-gate.ps1 tools/quality-gate.sh tools/unity-tests.sh \
  tools/unity-tests-mcp.sh tools/tests/quality-gate-selftest.ps1 \
  production/quality/test-manifest.json production/quality/gate-exceptions.json \
  docs/agent-token-hygiene.md
git commit -m "test: establish trustworthy gate 0 runner"
```

---

### 任务 2：修复 Bag 数据保真与安全模式

**优先级：** P0

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Bag/Runtime/BagMutationResult.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Bag/Runtime/IBagMutationMode.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Bag/Runtime/BagManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Bag/Data/BagData.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Bag/UI/BagPanel.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/Adapters/BagNetworkModeBinder.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 创建：`Assets/Scripts/Tests/Editor/Bag/BagManagerTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/Bag/BagOnlineReadOnlyIntegrationTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Bag/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`

- [ ] **步骤 1：编写失败的 Bag 测试**

测试名与核心断言：

```csharp
Compact_Preserves_Every_Item_Instance_Field();       // InstanceId/lock/expire/durability/level/bonus 不变
Compact_Preserves_ExtendedCapacity_And_SlotCount();  // Slots.Count == TotalCapacity
Compact_Does_Not_Merge_Any_Stacks_Or_Generate_InstanceIds();
LoadFromServer_Null_Returns_Failure_And_Keeps_Current_Data();
Public_Mutations_Before_Initialize_Return_NotInitialized();
AddItemDetailed_Returns_All_Changed_Slots();
OnlineReadOnly_Mode_Rejects_Local_Permanent_Mutation();
Entering_Online_Mode_Binds_Bag_ReadOnly_Before_Connect();
Online_World_Rejects_Every_Permanent_Bag_Write_Entry();
Returning_To_SinglePlayer_Reenables_Local_Bag_Mutations();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode BagManagerTests`
预期：FAIL；当前 Compact 丢元数据/扩展容量，且新 API 不存在。

- [ ] **步骤 3：实现数据保真 API**

新增签名：

```csharp
public readonly struct BagMutationResult
{
    public BagResult Result { get; }
    public IReadOnlyList<int> ChangedSlots { get; }
    public int RemainingCount { get; }
}

public BagMutationResult AddItemDetailed(int templateId, int count = 1);
public BagResult Compact();
public void SetOnlineReadOnly(bool value);
public interface IBagMutationMode { void SetOnlineReadOnly(bool value); }
```

`Compact` 只把现有实例引用稳定移到前方空槽，保持原相对顺序；绝不合并、拆分、重建 `ItemData` 或生成 ID。`LoadFromServer` 改为返回 `BagResult`，null/非法槽位数据失败时保留旧状态。旧 `AddItem` 调用详细 API 并标记 `[Obsolete]`。所有写入口统一检查初始化和 read-only 状态；`BagPanel` 未就绪时显示空态或隐藏。

`BagManager` 实现 `IBagMutationMode`。`BagNetworkModeBinder` 位于 SyncLayer 适配器层，由 `ClientNetworkHost` 组合根创建：启动时立即读取 `NetworkModeService.Load()`，在选择 Online 后、建立连接前切为只读；模式变化通过可释放订阅同步，只有明确回到 SinglePlayer 才恢复本地写入。断线不会自动解除只读，避免离线窗口产生无法与服务器对账的永久变更。

- [ ] **步骤 4：运行目标与全量回归**

```bash
bash tools/unity-tests.sh EditMode BagManagerTests
bash tools/unity-tests.sh PlayMode BagOnlineReadOnlyIntegrationTests
bash tools/unity-tests.sh EditMode
```

预期：Bag 新测试全过，EditMode 全量无新增失败。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Bag \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/Adapters/BagNetworkModeBinder.cs* \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md \
  Assets/Scripts/Tests/Editor/Bag/BagManagerTests.cs* \
  Assets/Scripts/Tests/PlayMode/Bag/BagOnlineReadOnlyIntegrationTests.cs*
git commit -m "fix(bag): preserve item metadata during mutations"
```

> **P0-A 波次 Gate 覆盖要求：** P0-A 波次 Gate 必须覆盖原 Spec 10.1 的 Bag/DamageContext 项。

---

### 任务 3：把技能配置与运行时伤害上下文分离

**优先级：** P0

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillDamageContext.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillDamageHelper.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitbox.cs`
- 创建：`Assets/Scripts/Tests/Editor/Skills/SkillDamageContextTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Combat/README.md`

- [ ] **步骤 1：编写失败的共享资产污染测试**

```csharp
Two_Executors_Sharing_SkillData_Do_Not_Mutate_DamageBlock();
Multiple_Targets_Get_Independent_Hit_Contexts();
Charged_Hit_Scales_Context_Not_Config();
AttackHitbox_Does_Not_Write_Knockback_Into_DamageBlock();
```

每个测试在施法/命中前后逐字段比较原 `DamageBlock`，并交错运行两个 executor，断言结果互不影响。

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode SkillDamageContextTests`
预期：FAIL；配置字段被 `FillDamageContext` 或 AttackHitbox 回写。

- [ ] **步骤 3：实现不可变运行时上下文**

新增签名：

```csharp
public readonly struct SkillDamageContext
{
    public int SkillId { get; }
    public int ComboIndex { get; }
    public float FinalDamage { get; }
    public float KnockbackForce { get; }
    public DamageType DamageType { get; }
}

public static SkillDamageContext CreateContext(
    DamageBlock config, int skillId, int comboIndex, float chargeScale);

public static DamageEvent CreateDamageEvent(
    in SkillDamageContext context, Vector3 hitDirection, bool isLocalSource);
```

删除/废弃任何会写入 `DamageBlock` 的 `FillDamageContext` 路径。`SkillExecutor` 每次施法创建基础 context，每个目标只派生事件；Combat 适配器只消费 context。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode SkillDamageContextTests
bash tools/unity-tests.sh EditMode SkillExecutor
bash tools/unity-tests.sh EditMode SwordQiDamageBlockCloneTests
```

预期：所有目标测试通过，既有蓄力/火球/陨石/旋转/剑气结果不变。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills \
  Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitbox.cs \
  Assets/Scripts/Hotfix/GameSystems/Combat/README.md \
  Assets/Scripts/Tests/Editor/Skills/SkillDamageContextTests.cs*
git commit -m "fix(skills): isolate runtime damage context"
```

> **P0-A 波次 Gate 覆盖要求：** P0-A 波次 Gate 必须覆盖原 Spec 10.1 的 Bag/DamageContext 项。

---

### 任务 4：修复 EventBus 生命周期与技能启动事务

**优先级：** P0

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Skills/EventSubscription.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/EventBus.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`
- 创建：`Assets/Scripts/Tests/Editor/Skills/EventBusLifecycleTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Skills/SkillActivationTransactionTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/README.md`

`EventSubscription` 采用 `sealed class`（内部共享原子退订状态，副本只持有同一状态引用）；U-P0-05 的跨副本/重复 Dispose 与零分配断言写入 `EventBusLifecycleTests.cs`。

- [ ] **步骤 1：编写失败的生命周期/事务测试**

```csharp
ResetAll_Clears_Normal_Targeted_And_Pause_State();
Dispatch_Allows_Callback_To_Remove_Another_Callback_Safely();
Disposed_Subscription_Is_Idempotent_And_Prunes_Empty_Bucket();
Copied_Tokens_Dispose_Only_Once();
Duplicate_Dispose_Does_Not_Throw();
Emit_Hot_Path_Allocates_No_Temporary_Arrays();
SubsystemRegistration_Resets_Managed_State();
Failed_TryStart_Does_Not_Consume_Stamina_Or_Start_Cooldown();
Completed_Executor_Unsubscribes_Exactly_Once();
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode EventBusLifecycleTests
bash tools/unity-tests.sh EditMode SkillActivationTransactionTests
```

预期：FAIL；targeted/pause 残留、委托无法退订或资源提前提交。

- [ ] **步骤 3：实现订阅 token 与事务顺序**

实现签名：

```csharp
public sealed class EventSubscription : IDisposable { public void Dispose(); }
public static EventSubscription Subscribe<T>(Action<T> callback) where T : IEvent;
public static EventSubscription SubscribeTargeted<T>(int targetId, Action<T> callback) where T : IEvent;
public static void ResetAll();

private bool TryActivateSkill(int skillId, SkillInput input);
private void CommitActivation(SkillExecutor executor, SkillData data);
```

`EventSubscription` 为 `sealed class`，内部共享一份订阅状态，用 `Interlocked.Exchange`（或等价原子状态）标记已退订；token 副本或重复 `Dispose` 只执行一次退订且不抛异常。EventBus 对每个事件类型维护 copy-on-write 委托快照：订阅/退订复制并原子替换快照，`Emit` 只读取一次快照，热路径不调用 `ToArray()`、不分配临时数组，回调可安全增删订阅。`ResetAll` 清普通订阅、定向订阅与暂停状态，并回收归零的空 bucket；添加 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` 重置方法调用 `ResetAll()`，只清纯托管状态。SkillCoordinator 先验证并让 executor `TryStart()` 成功，再提交精力、冷却和活动注册；保存 token/真实委托实例，在完成、中断、Dispose 时释放。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode EventBusLifecycleTests
bash tools/unity-tests.sh EditMode SkillActivationTransactionTests
bash tools/unity-tests.sh EditMode SkillCoordinator
```

预期：新测试和 SkillCoordinator 既有测试全过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills \
  Assets/Scripts/Tests/Editor/Skills/EventBusLifecycleTests.cs* \
  Assets/Scripts/Tests/Editor/Skills/SkillActivationTransactionTests.cs*
git commit -m "fix(skills): make subscriptions and activation transactional"
```

---

### 任务 5：收口为单一 Global 世界并统一 PlayerState

**优先级：** P0

**文件：**
- 创建：`Server/Config/ServerWorldOptions.cs`
- 修改：`Server/Room/RoomManager.cs`
- 修改：`Server/Room/Room.cs`
- 修改：`Server/Handler/LoginHandler.cs`
- 修改：`Server/Server3C.cs`
- 修改：`Server/Player/PlayerState.cs`
- 创建：`Server/Server.Tests/World/SingleWorldContainmentTests.cs`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的单世界不变量测试**

```csharp
Unknown_Zone_Is_Rejected_Without_Creating_Room_Or_Entity();
Room_And_EntityStore_Reference_The_Same_PlayerState();
MaxRooms_And_MaxPlayers_Are_Enforced();
Zone_Name_Over_Limit_Is_Rejected();
GetOtherPlayerSessions_Excludes_By_PlayerId_Not_SessionId();
Empty_Room_Is_Recycled_After_Last_Player_Leaves();
ChangeZone_Is_Disabled_Until_WorldInstance_Is_Available();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~SingleWorldContainmentTests"`
预期：FAIL；任意 Zone 可建 Room，且存在双份 PlayerState。

- [ ] **步骤 3：实现安全收口**

实现签名：

```csharp
public sealed record ServerWorldOptions(
    IReadOnlySet<string> AllowedWorldIds,
    int MaxRooms,
    int MaxPlayersPerRoom,
    int MaxWorldIdLength);

public bool TryGetOrCreateRoom(string roomId, out Room room, out RoomRejectReason reason);
public bool RegisterEntity(PlayerState state);
public bool TryChangePlayerZone(...); // 仅同 world 幂等成功；跨 world 返回 false
```

登录必须先验证 world，再创建唯一 `PlayerState`，同一实例同时交给 Room 和 EntityStore。修正 `GetOtherPlayerSessions` 的 ID 域。空房回收和 MaxRooms 真正使用配置；默认白名单只有 `Global`。`TryChangePlayerZone` 在 `WorldInstance` 可用前返回“不支持”，不得先删除旧 Room/旧实体再尝试创建新房。

- [ ] **步骤 4：验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~SingleWorldContainmentTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~LoginRosterTests|FullyQualifiedName~Aoi"
```

预期：新测试与既有登录/AOI 测试全过。

**执行设计 §4.3 验收补齐（任务 5）：** 登录只接受白名单 `Global`；Room 与 `EntityStore` 使用同一 `PlayerState`；`MaxRooms`、最大人数、Zone 名称长度和空房回收生效；换区返回“不支持”，不得先删旧房。

- [ ] **步骤 5：Commit**

```bash
git add Server/Config/ServerWorldOptions.cs Server/Room Server/Handler/LoginHandler.cs \
  Server/Server3C.cs Server/Player/PlayerState.cs \
  Server/Server.Tests/World/SingleWorldContainmentTests.cs Server/README.md
git commit -m "fix(server): contain runtime to one authoritative world"
```

---

### 任务 6：实现幂等登录与可替换票据认证

**优先级：** P0；依赖任务 5

**文件：**
- 修改：`Assets/Scripts/AOT/Protocol/Messages/Login.cs`
- 修改：`Assets/Scripts/AOT/Protocol/ProtocolConstants.cs`
- 创建：`Server/Auth/ILoginTicketValidator.cs`
- 创建：`Server/Auth/LoginIdentity.cs`
- 创建：`Server/Auth/HmacLoginTicketValidator.cs`
- 创建：`Server/Auth/DevelopmentLoginTicketValidator.cs`
- 创建：`Server/Session/ServerSessionContext.cs`
- 修改：`Server/Session/SessionManager.cs`
- 修改：`Server/Handler/LoginHandler.cs`
- 修改：`Server/Program.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientLoginOptions.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- 创建：`Server/Server.Tests/Auth/LoginIdempotencyTests.cs`
- 创建：`Server/Server.Tests/Auth/LoginTicketValidatorTests.cs`
- 修改：`Server/Server.Tests/Protocol/CodecRoundtripTests.cs`（移除 `md5_hash_placeholder`）
- 修改：`Assets/Scripts/Examples/ClientExample.cs`（移除 `md5_hash_placeholder`）
- 修改：`Assets/Scripts/AOT/Protocol/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的协议与登录测试**

```csharp
Repeated_RequestId_Returns_Same_Response_And_Entity();
Authenticated_Session_Cannot_Switch_Character();
Invalid_Or_Expired_Ticket_Creates_No_State();
Ticket_Signature_Covers_AllowedWorldId();
Requested_Zone_Not_In_Ticket_Is_Rejected_Without_State();
Same_Character_Second_Session_Is_Rejected();
Disconnect_Cleans_All_Identity_Maps_Exactly_Once();
Disconnect_Cleanup_Is_Idempotent_By_Stable_CharacterId();
Production_Profile_Rejects_Development_Validator();
Login_V2_Fields_RoundTrip_On_Unity_And_Server();
Sensitive_Ticket_Is_Not_Written_To_Logs();
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~LoginIdempotencyTests|FullyQualifiedName~LoginTicketValidatorTests"
bash tools/unity-tests.sh EditMode ProtocolRoundtripTests
```

预期：FAIL；新字段/validator/session 状态机不存在。

- [ ] **步骤 3：追加协议并实现认证边界**

协议追加：

```csharp
// LoginRequest tags 7-9
public ulong RequestId { get; set; }
public string AuthTicket { get; set; }
public long CharacterId { get; set; }

// LoginResponse tags 12-13
public ulong RequestId { get; set; }
public LoginRejectCode RejectCode { get; set; }
```

服务端接口：

```csharp
public interface ILoginTicketValidator
{
    ValueTask<LoginIdentity?> ValidateAsync(
        string ticket, long requestedCharacterId, CancellationToken cancellationToken);
}

public enum ServerSessionState { Connected, Authenticating, InWorld, Closing, Closed }
```

HMAC 票据格式固定为 `v1.accountId.characterId.allowedWorldId.expiresUnixSeconds.nonce.signature`；`accountId`、`characterId`、`allowedWorldId`、到期时间与 nonce 全部进入签名载荷。HMAC-SHA256 密钥来自 `COOL_AUTH_HMAC_KEY`，至少 32 字节。validator 权威构造 `LoginIdentity(AccountId, CharacterId, AllowedWorldId)`；`LoginRequest.CharacterId/ZoneId` 只能作为一致性声明，任一值与 identity 不匹配都拒绝且不得创建 session/player/world 状态。`--allow-dev-auth` 只在 Debug/loopback 启用；生产未配置 validator 直接拒绝启动。`ClientNetworkManager.ConnectAndLoginAsync(ClientLoginOptions, CancellationToken)` 替换 `async void` 和占位密码；同时删除 `ClientNetworkManager`、`ClientExample` 与协议 fixture 中的 `md5_hash_placeholder`，生产登录只发送 `RequestId`/`AuthTicket`/`CharacterId`，日志不得输出票据或密码。断线清理以稳定 `CharacterId` 为键幂等执行，重复断线或重复登录响应不得重复删除实体或身份映射。

同时把 `ProtocolConstants.ProtocolVersion` 提升为 `2`：生产 profile 明确拒绝 v1；开发 profile 只有在显式诊断开关开启时才解析 v1，并且仍必须先通过票据认证，禁止任何无鉴权降级路径。

- [ ] **步骤 4：验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Login"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~VersionRejectTests"
bash tools/unity-tests.sh EditMode ProtocolRoundtripTests
```

预期：登录、版本拒绝与协议 roundtrip 全过；重复请求实体数恒定。

验收：`grep -R "md5_hash_placeholder" Assets/Scripts Server` 无输出。

**执行设计 §4.3 验收补齐（任务 6）：** `Connected → Authenticating → InWorld → Closing` 状态机；`LoginRequest` 含 `RequestId`、`AuthTicket`、`CharacterId`；同一 RequestId 重传返回同一结果；HMAC-SHA256 票据验证器权威给出身份；开发票据仅 loopback/Debug；生产未配置验证器拒绝启动；移除 `md5_hash_placeholder`；`ProtocolVersion` 升到 2；断线清理按稳定 CharacterId 幂等。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/AOT/Protocol Assets/Scripts/Hotfix/GameSystems/SyncLayer \
  Assets/Scripts/Examples/ClientExample.cs* \
  Server/Auth Server/Session Server/Handler/LoginHandler.cs Server/Program.cs \
  Server/Server.Tests/Auth Server/Server.Tests/Protocol/CodecRoundtripTests.cs \
  Server/README.md
git commit -m "feat(auth): add idempotent ticket-based login"
```

---

### 任务 7：实现服务器时间驱动的移动校验 v2

**优先级：** P0；依赖任务 5、6

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncCore/SequenceMath.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/MovementValidator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/MovementReporter.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncLayer/MovementReporterTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- 创建：`Server/Ingress/InboundEnvelope.cs`
- 创建：`Server/Movement/IMovementConstraint.cs`
- 创建：`Server/Movement/AuthoritativeMovementService.cs`
- 修改：`Server/GameLoop.cs`
- 修改：`Server/Server3C.cs`
- 修改：`Server/WebSocketServerListener.cs`
- 修改：`Server/Handler/PositionSyncHandler.cs`
- 修改：`Server/Player/PlayerState.cs`
- 修改：`Assets/Scripts/Tests/Editor/SyncCore/MovementValidatorTests.cs`
- 修改：`Server/Server.Tests/SyncCore/MovementValidatorTests.cs`
- 创建：`Server/Server.Tests/Movement/AuthoritativeMovementTests.cs`
- 创建：`Server/Server.Tests/Ingress/InboundReceiveTimeTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Server/README.md`

- [ ] **步骤 1：先改测试锁定新语义**

删除固化旧漏洞的 “50m 内接受”“dt=0 跳过”断言，改为：

```csharp
SequenceMath_Accepts_Strictly_Newer_Value_Across_Wrap();
Duplicate_And_Older_Sequence_Do_Not_Mutate_State();
Client_Timestamp_Cannot_Change_Move_Budget();
Zero_Client_Delta_Cannot_Bypass_Server_Elapsed_Time();
Sub_50m_Burst_Is_Clamped();
Excessive_Upward_Y_Is_Clamped();
NonFinite_Input_Reuses_Last_Position_And_Rotation();
Long_Silence_Does_Not_Accumulate_Unbounded_Budget();
Receive_Time_Is_Captured_Before_Queue_And_Decode();
Queue_Delay_Does_Not_Increase_The_Next_Move_Budget();
Elapsed_Is_Clamped_Between_OneOver120_And_QuarterSecond();
Rotation_Is_Normalized_To_ZeroInclusive_360Exclusive();
Projection_Over_HalfMeter_Or_NonWalkable_Segment_Is_Rejected();
Dead_Player_Cannot_Move();
MovementReporter_Uses_Synchronized_Server_Clock_For_Diagnostics();
Injected_Network_Delay_Does_Not_Block_MainThread();
```

- [ ] **步骤 2：运行双端测试验证失败**

```bash
bash tools/unity-tests.sh EditMode MovementValidatorTests
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MovementValidatorTests|FullyQualifiedName~AuthoritativeMovementTests|FullyQualifiedName~InboundReceiveTimeTests"
```

预期：FAIL；当前规则依赖客户端 timestamp，且 Y 未钳制。

- [ ] **步骤 3：实现共享规则与服务端应用服务**

实现签名：

```csharp
public static bool SequenceMath.IsNewer(uint candidate, uint baseline);

public readonly struct MovementValidationInput
{
    public Vec3 Previous { get; }
    public Vec3 Requested { get; }
    public float RequestedRotation { get; }
    public float RequestedSpeed { get; }
    public float ServerElapsedSeconds { get; }
    public float JitterAllowanceMeters { get; }
}

public static MoveValidationResult MovementValidator.Validate(in MovementValidationInput input);

public interface IMovementConstraint
{
    bool TryConstrain(Vec3 previous, Vec3 requested, out Vec3 constrained);
}

public MovementApplyResult AuthoritativeMovementService.Apply(
    PlayerState player, PositionSyncRequest request, long receivedMonotonicTicks);
```

`InboundEnvelope` 在每条传输的收包入口、排队和 `MessageCodec.Decode` 之前用 `Stopwatch.GetTimestamp()` 捕获 `ReceivedMonotonicTicks`，随后沿 GameLoop/dispatch context 传给 handler；任务 9 只把同一 envelope 的队列改为有界，不重新定义时间源。`PlayerState` 增加 `LastAcceptedMoveReceivedTicks`，只在 sequence 严格更新且移动被接受时推进。

elapsed 使用相邻两次已接受包的单调接收时间，并钳制到 `[1/120s, 0.25s]`。水平预算固定为 `MaxSpeed * elapsed * 1.2 + 0.25m`，向上预算为 `8m/s * elapsed + 0.25m`；旋转归一化到 `[0, 360)`。终点必须能在最多 `0.5m` 内投影到 navmesh，且从上一权威位置到投影点的线段可行走；本路径不得为每个移动包执行完整 A*。非新 sequence 只回当前权威 ack，排队延迟不能给下一包增加预算。从生产 `MovementReporter` 删除 `injectedSendDelayMs` 构造函数参数与 `Thread.Sleep`；`DateTime.UtcNow` 改为可注入时钟（宿主传入已同步的服务器时钟），客户端时间戳只作诊断。延迟、抖动与丢包模拟只放在 fake transport 或可控 test scheduler 中，`Injected_Network_Delay_Does_Not_Block_MainThread` 证明注入网络延迟不阻塞 Unity 主线程。地图约束失败时保持上一位置。服务端移动校验 v2 的其余规则（sequence、elapsed 钳制、预算、投影/可达性）不变。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode MovementValidatorTests
bash tools/unity-tests.sh EditMode MovementReporterTests
bash tools/unity-tests.sh PlayMode MovementSyncTests
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Movement|FullyQualifiedName~InboundReceiveTimeTests"
```

预期：双端规则测试和移动 PlayMode 全过；所有权威结果无 NaN/Infinity。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/MovementReporter.cs \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs \
  Assets/Scripts/Tests/Editor/SyncCore/MovementValidatorTests.cs \
  Assets/Scripts/Tests/Editor/SyncLayer/MovementReporterTests.cs \
  Server/Ingress/InboundEnvelope.cs Server/Movement Server/GameLoop.cs Server/Server3C.cs \
  Server/WebSocketServerListener.cs Server/Handler/PositionSyncHandler.cs Server/Player/PlayerState.cs \
  Server/Server.Tests/SyncCore/MovementValidatorTests.cs \
  Server/Server.Tests/Movement/AuthoritativeMovementTests.cs \
  Server/Server.Tests/Ingress/InboundReceiveTimeTests.cs Server/README.md
git commit -m "fix(sync): validate movement by server time and sequence"
```

---

### 任务 8：把格挡与精力迁到服务端权威状态机

**优先级：** P0；依赖任务 5、6，可与任务 7 并行

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncCore/StaminaRules.cs`
- 创建：`Assets/Scripts/AOT/Protocol/Messages/Guard.cs`
- 修改：`Assets/Scripts/AOT/Protocol/MessageId.cs`
- 修改：`Assets/Scripts/AOT/Protocol/MessageCodec.cs`
- 修改：`Assets/Scripts/AOT/Protocol/Messages/PositionSync.cs`
- 创建：`Server/Combat/GuardSystem.cs`
- 创建：`Server/Combat/GuardState.cs`
- 创建：`Server/Handler/GuardHandler.cs`
- 修改：`Server/Player/PlayerState.cs`
- 修改：`Server/Handler/PositionSyncHandler.cs`
- 修改：`Server/Handler/CastHandler.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/GuardReporter.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncCore/StaminaRulesTests.cs`
- 创建：`Server/Server.Tests/Combat/ServerGuardTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/ServerGuardAuthorityTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Stamina/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的服务端权威测试**

```csharp
PositionSync_IsDefending_Cannot_Activate_Guard();
Permanent_PositionSync_Defend_True_Cannot_Sustain_Guard();
DefendCommand_Start_Stop_Uses_Sequence_Without_Replaying_Old_Intent();
Guard_Start_Captures_Facing_And_Death_Cleans_Up_State();
Duplicate_Guard_Intent_Is_Idempotent();
Guard_Drains_Stamina_By_Server_Tick();
Blocked_Hit_Consumes_Server_Stamina();
Zero_Stamina_Forces_Guard_Off();
Death_Respawn_Disconnect_Clear_Or_Refill_Guard_As_Specified();
Remote_Snapshot_Uses_Authoritative_Guard_State();
Client_Prediction_Reconciles_To_Guard_Response();
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ServerGuardTests"
bash tools/unity-tests.sh EditMode StaminaRulesTests
```

预期：FAIL；Guard 消息/系统不存在，PositionSync 仍直接写状态。

- [ ] **步骤 3：实现共享数值规则与 Guard 协议**

协议与接口：

```csharp
public enum DefendAction { Start = 0, Stop = 1 }

public sealed class DefendCommand : IMessage
{
    public uint Sequence { get; set; }
    public DefendAction Action { get; set; }
}

public sealed class GuardStateResponse : IMessage
{
    public uint AcknowledgedSequence { get; set; }
    public bool Active { get; set; }
    public float Stamina { get; set; }
    public uint ServerTick { get; set; }
    public GuardRejectReason RejectReason { get; set; }
}

public sealed class GuardSystem
{
    public GuardTransition ApplyIntent(GuardState state, DefendCommand command, uint tick);
    public void Tick(GuardState state, uint tick);
    public bool TryConsumeBlockedHit(GuardState state, float cost, uint tick);
}
```

`SyncCore/StaminaRules` 使用 `System.Math`，成为两端数值真值；客户端 `StaminaSystem` 包装同一规则。服务端只接受独立的 `DefendCommand`，在 `Start` 时捕获起手状态与朝向，在 `Stop`/死亡/断线/精力归零时收手并清理；精力消耗由服务端 tick 与命中结算权威扣减。旧 `PositionSyncRequest.IsDefending` 仅兼容读取并忽略，持续发送 true 不能激活或维持格挡。客户端可预测动画，但 UI/状态最终采信 `GuardStateResponse` 与 snapshot。

- [ ] **步骤 4：验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Guard|FullyQualifiedName~Block"
bash tools/unity-tests.sh EditMode StaminaRulesTests
bash tools/unity-tests.sh PlayMode ServerGuardAuthorityTests
bash tools/unity-tests.sh PlayMode BlockMitigationE2ETests
```

预期：服务端权威、双端精力规则和既有格挡表现全过。

**执行设计 §4.3 验收补齐（任务 8）：** 独立 `DefendCommand(Start/Stop, sequence)`；服务端持有起手、收手、朝向、死亡清理和精力消耗；客户端永久发送防御 true 不能获得无限格挡；精力归零后服务端强制结束防御；旧 `IsDefending` 仅兼容读取并忽略。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore Assets/Scripts/AOT/Protocol \
  Assets/Scripts/Hotfix/GameSystems/SyncLayer Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
  Assets/Scripts/Hotfix/GameSystems/Stamina/README.md Assets/Scripts/Tests \
  Server/Combat Server/Handler Server/Player/PlayerState.cs Server/Server.Tests/Combat Server/README.md
git commit -m "feat(combat): make guard and stamina server authoritative"
```

---

### 任务 9：把 GameLoop 改为有界、按 tick 预算消费

**优先级：** P0；依赖任务 7

**文件：**
- 修改：`Server/Ingress/InboundEnvelope.cs`
- 创建：`Server/Ingress/IngressOptions.cs`
- 创建：`Server/Ingress/IngressResult.cs`
- 修改：`Server/GameLoop.cs`
- 修改：`Server/Server3C.cs`
- 创建：`Server/Diagnostics/ServerMetrics.cs`
- 创建：`Server/Server.Tests/Ingress/BoundedGameLoopTests.cs`
- 创建：`tools/ServerLoadHarness/ServerLoadHarness.csproj`
- 创建：`tools/ServerLoadHarness/Program.cs`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的洪泛与公平性测试**

```csharp
Continuous_Producer_Does_Not_Starve_Ticks();
Full_Queue_Returns_Full_Without_Growing();
Per_Tick_Budget_Preserves_Fifo_Remainder();
Action_Flood_Does_Not_Starve_Data();
Reliable_Command_Overflow_Is_Not_Silent();
Handler_Exception_Does_Not_Abort_Remaining_Budget();
Stop_Rejects_New_Work_And_Completes_Within_Deadline();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~BoundedGameLoopTests"`
预期：FAIL；当前队列无界且同 tick 追赶新入队项。

- [ ] **步骤 3：实现有界 ingress 与 tick 快照预算**

实现签名：

```csharp
public sealed record IngressOptions(
    int Capacity,
    int MaxPacketsPerTick,
    int MaxActionsPerTick,
    TimeSpan MaxWorkTime,
    TimeSpan TickInterval);

public IngressResult TryEnqueue(InboundEnvelope envelope);
public IngressResult TryEnqueueAction(Action action);
```

复用任务 7 已在收包入口填好的 `InboundEnvelope.ReceivedMonotonicTicks`，排队、解码和预算消费不得覆盖它。使用有界 `Channel` 或固定容量环形队列；每 tick 先读取本轮可消费数量快照，并在 2048 条或 20ms 处停止。控制队列有保留容量但受 `MaxActionsPerTick` 限制。记录 queue depth/full/drop/tick duration，禁止逐包洪泛日志。

- [ ] **步骤 4：验证通过与 10 分钟 soak**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~BoundedGameLoopTests|FullyQualifiedName~GameLoopTests"
dotnet run -c Release --project tools/ServerLoadHarness -- --duration 600 --flood-ingress
```

预期：tick 持续推进；队列不超过 8192；working set 无单调增长；所有拒绝有指标。

- [ ] **步骤 5：Commit**

```bash
git add Server/Ingress Server/Diagnostics/ServerMetrics.cs Server/GameLoop.cs \
  Server/Server3C.cs Server/Server.Tests/Ingress/BoundedGameLoopTests.cs Server/README.md \
  tools/ServerLoadHarness
git commit -m "fix(server): bound ingress work per tick"
```

---

### 任务 10：统一准入、WebSocket 背压与协议标志安全

**优先级：** P0；依赖任务 9

**文件：**
- 创建：`Server/Ingress/AdmissionOptions.cs`
- 创建：`Server/Ingress/PeerKey.cs`
- 创建：`Server/Ingress/TokenBucket.cs`
- 创建：`Server/Ingress/IngressAdmissionController.cs`
- 修改：`Server/Session/SessionManager.cs`
- 修改：`Server/WebSocketServerListener.cs`
- 修改：`Server/WebSocketServerSession.cs`
- 修改：`Server/Server3C.cs`
- 修改：`Assets/Scripts/AOT/Protocol/MessageCodec.cs`
- 创建：`Server/Server.Tests/Ingress/AdmissionControllerTests.cs`
- 创建：`Server/Server.Tests/WebSocket/WebSocketBackpressureTests.cs`
- 修改：`Server/Server.Tests/Protocol/CodecRoundtripTests.cs`
- 修改：`Assets/Scripts/Tests/Editor/Network/ProtocolRoundtripTests.cs`
- 修改：`Assets/Scripts/AOT/Protocol/README.md`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的边界测试**

```csharp
PerIp_And_PerSession_TokenBuckets_Use_Spec_Rates();
Oversized_Fragmented_WebSocket_Message_Closes_Connection();
Fragment_Assembly_Timeout_Closes_Connection();
Slow_WebSocket_Client_Hits_Frame_Or_Byte_Cap();
WebSocket_Session_Is_Owned_By_SessionManager_And_Closed_On_Stop();
PositionSync_Admission_Keeps_Highest_Sequence();
Reliable_Control_Overflow_Closes_After_Strike_Limit();
Decode_Rejects_Encrypted_Flag_Without_Real_Provider();
Decode_Rejects_Compressed_Flag_Without_Real_Provider();
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~AdmissionControllerTests|FullyQualifiedName~WebSocketBackpressureTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CodecRoundtripTests"
```

预期：FAIL；WS 聚合/发送无界，flag 占位实现被接受。

- [ ] **步骤 3：实现两级准入与有限 WS 会话**

实现签名：

```csharp
public interface IIngressAdmissionController
{
    AdmissionDecision AdmitPeer(PeerKey peer, int byteCount, bool authenticated, long nowTicks);
    AdmissionDecision AdmitMessage(long sessionId, MessageId id, int byteCount, long nowTicks);
}

public sealed class WebSocketServerSession : IServerSession
{
    public const int MaxMessageBytes = 65_536;
    public const int MaxQueuedFrames = 256;
    public const int MaxQueuedBytes = 4 * 1024 * 1024;
}
```

按 Spec §6.0.2 写死默认 token-bucket 数值并允许配置覆盖。WS 分片累计超过 64KiB 或 5 秒未结束即关闭；待发达到帧/字节任一上限即断慢客户端。SessionManager 统一拥有 WS/KCP。`MessageCodec` 在没有真实 provider 时拒绝 Encrypted/Compressed；移除 XOR/直通压缩能力声明。

- [ ] **步骤 4：验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Admission|FullyQualifiedName~WebSocket|FullyQualifiedName~Codec"
bash tools/unity-tests.sh EditMode ProtocolRoundtripTests
```

预期：所有上限/超时/flag 测试通过；服务端停机关闭已接受 WS。

- [ ] **步骤 5：Commit**

```bash
git add Server/Ingress Server/Session Server/WebSocketServer* Server/Server3C.cs \
  Server/Server.Tests/Ingress Server/Server.Tests/WebSocket \
  Assets/Scripts/AOT/Protocol Assets/Scripts/Tests/Editor/Network/ProtocolRoundtripTests.cs \
  Server/Server.Tests/Protocol/CodecRoundtripTests.cs Server/README.md
git commit -m "fix(network): enforce admission and websocket backpressure"
```

---

### 任务 11：服务端 KCP 单执行上下文与 cookie 准入

**优先级：** P0；依赖任务 9、10

**文件：**
- 创建：`Server/Transport/Kcp/KcpIoLoop.cs`
- 创建：`Server/Transport/Kcp/KcpSessionState.cs`
- 创建：`Server/Transport/Kcp/KcpCommand.cs`
- 创建：`Server/Transport/Kcp/UdpCookieService.cs`
- 修改：`Server/KcpServerSession.cs`
- 修改：`Server/Session/SessionManager.cs`
- 修改：`Server/Server3C.cs`
- 创建：`Server/Server.Tests/Kcp/KcpIoOwnershipTests.cs`
- 创建：`Server/Server.Tests/Kcp/UdpCookieAdmissionTests.cs`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的所有权与 DoS 测试**

```csharp
Unknown_Udp_Peer_Does_Not_Allocate_Kcp_State_Before_Valid_Cookie();
All_Kcp_Mutations_Run_On_One_Owner_Loop();
Concurrent_Input_And_Send_Preserve_Message_Boundaries();
One_Thousand_Idle_Sessions_Use_One_Scheduler_Task();
Close_During_Update_Fires_Disconnected_Exactly_Once();
Disposed_Session_Rejects_Late_Commands();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~KcpIoOwnershipTests|FullyQualifiedName~UdpCookieAdmissionTests"`
预期：FAIL；首包直接建 session，KCP 被多个执行上下文触达。

- [ ] **步骤 3：实现中央 KCP owner**

只有 `KcpIoLoop` 可以调用 KCP 的 `Input/Update/Send/TryRecv/Dispose`。主循环通过 `KcpCommand.Send/Close` 入有界命令队列；完整应用帧通过任务 9 的 ingress 投递。`UdpCookieService` 使用服务器 secret + peer + 时间窗生成无状态 HMAC cookie，验证成功后才分配 KCP state；全局 512、单 IP 8 个未认证上限仍保留。

- [ ] **步骤 4：验证通过与压力测试**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Kcp"
dotnet run -c Release --project tools/ServerLoadHarness -- --duration 600 --udp-garbage --kcp-sessions 1000
```

预期：无 per-session 100Hz Task、无并发损坏、垃圾 UDP 不增长 KCP state。

- [ ] **步骤 5：Commit**

```bash
git add Server/Transport/Kcp Server/KcpServerSession.cs Server/Session/SessionManager.cs \
  Server/Server3C.cs Server/Server.Tests/Kcp Server/README.md tools/ServerLoadHarness
git commit -m "fix(kcp): serialize server kcp ownership and admission"
```

---

### 任务 12：客户端网络统一主线程消息泵

**优先级：** P0；可与任务 11 并行

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientMessagePump.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/IClientMessageBus.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/MessageDispatcher.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncLayer/ClientMessagePumpTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/SyncLayer/MessageDispatcherLifecycleTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/ClientNetworkThreadConfinementTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`

- [ ] **步骤 1：编写失败的线程/容量测试**

```csharp
WorkerThread_Message_Dispatches_Only_During_MainThread_Update();
Drain_Processes_At_Most_64_And_At_Most_TwoMilliseconds();
Queue_Never_Exceeds_512_Entries();
Overflow_Sets_One_Fatal_Signal_And_Forces_World_Resync();
WorldSnapshots_Preserve_FIFO_Because_FieldMasks_Are_Incremental();
Handler_Unsubscribes_During_Dispatch_Uses_Start_Snapshot();
Dispose_Drops_Late_Transport_Callbacks();
CombatBridge_Does_Not_Subscribe_Directly_To_Adapter();
Concurrent_ConnectAndLogin_Starts_One_Connection();
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode ClientMessagePumpTests
bash tools/unity-tests.sh EditMode MessageDispatcherLifecycleTests
```

预期：FAIL；当前四个队列无界，部分事件仍在回调线程触发。

- [ ] **步骤 3：实现有界 MPSC 泵与可取消登录**

实现签名：

```csharp
internal sealed class ClientMessagePump : IDisposable
{
    internal const int Capacity = 512;
    internal bool TryEnqueue(ClientEnvelope envelope);
    internal DrainResult Drain(int maxMessages, TimeSpan maxDuration, Action<ClientEnvelope> dispatch);
    internal bool ConsumeOverflowSignal();
}

public interface IClientMessageBus
{
    IDisposable Subscribe(MessageId id, Action<IMessage> handler);
}

public Task<LoginResult> ConnectAndLoginAsync(
    ClientLoginOptions options, CancellationToken cancellationToken);
```

adapter 的 Message/Disconnected/Error 只入泵；主线程 `Update` 以 64 条/2ms 排放。当前 WorldSnapshot 含增量掩码，保持 FIFO，不能 latest-wins。任何溢出在下一主线程帧主动断线并重建世界。移除 `_pendingSnapshots/_pendingNames/_pendingLeaves/_pendingResponses`；CombatBridge 订阅 manager bus。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode ClientMessagePumpTests
bash tools/unity-tests.sh EditMode MessageDispatcherLifecycleTests
bash tools/unity-tests.sh PlayMode ClientNetworkThreadConfinementTests
bash tools/unity-tests.sh PlayMode ThreeClientCombatConsistencyTests
```

预期：所有业务事件在主线程；10,000 条伪消息内存有界并触发一次显式重同步。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer \
  Assets/Scripts/Tests/Editor/SyncLayer/ClientMessagePumpTests.cs* \
  Assets/Scripts/Tests/Editor/SyncLayer/MessageDispatcherLifecycleTests.cs* \
  Assets/Scripts/Tests/PlayMode/ClientNetworkThreadConfinementTests.cs*
git commit -m "fix(sync): confine client networking to bounded main-thread pump"
```

---

### 任务 13：硬化客户端 KCP 生命周期并保持实验性默认禁用

**优先级：** P1 安全项；P0 Gate 要求确认默认生产不启用 KCP；依赖任务 10、11

**文件：**
- 修改：`Assets/Scripts/AOT/KcpNet/Common/KcpOptions.cs`
- 修改：`Assets/Scripts/AOT/KcpNet/Common/KcpTransportBase.cs`
- 修改：`Assets/Scripts/AOT/KcpNet/Common/KcpSession.cs`
- 修改：`Assets/Scripts/AOT/KcpNet/Client/KcpClientTransport.cs`
- 修改：`Assets/Scripts/AOT/KcpNet/Client/KcpClient.cs`
- 修改：`Assets/Scripts/AOT/KcpNet/KcpNetClientAdapter.cs`
- 修改：`Assets/Scripts/AOT/KcpNet/Common/IKcpTransport.cs`
- 创建：`Assets/Scripts/AOT/KcpNet/Common/PooledBuffer.cs`
- 创建：`Assets/Scripts/AOT/KcpNet/Common/KcpBackpressureException.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTransportFactory.cs`
- 创建：`Assets/Scripts/Tests/Editor/Network/KcpClientSingleConsumerTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Network/KcpBackpressureTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Network/KcpShutdownTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Network/KcpBufferReuseTests.cs`
- 创建：`tools/kcp-soak.sh`
- 修改：`Assets/Scripts/AOT/KcpNet/README.md`

- [ ] **步骤 1：编写失败的单消费者/生命周期测试**

```csharp
Connect_Starts_Exactly_One_Receive_Consumer();
Input_Send_Update_TryRecv_Run_On_One_Driver();
SendQueue_Rejects_257th_Message_Or_Over_OneMiB();
Pooled_Buffer_Returns_On_Success_Exception_And_Cancel();
Shutdown_Completes_Within_OneSecond();
Dispose_On_MainThread_Does_Not_Block_FiveSeconds();
SendFailure_Raises_Observed_Adapter_Error();
ProductionFactory_Defaults_All_Platforms_To_WebSocket();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode KcpClientSingleConsumerTests`
预期：FAIL；存在双消费者、无界发送或阻塞 Dispose。

- [ ] **步骤 3：实现单 driver 与有界发送**

`NetworkDriverLoop` 成为客户端 KCP 唯一 owner；删除第二 receive loop。`KcpOptions` 增加 `MaxQueuedSendMessages=256`、`MaxQueuedSendBytes=1MiB`、`SendEnqueueTimeoutMs=50`、`MaxMessageBytes=65536`。复用 UDP buffer/writer，公开 `ShutdownAsync`，同步 Dispose 仅短兜底。`NetworkTransportFactory` 默认 WSS；KCP 只在显式实验配置且非 WebGL 时选择。

- [ ] **步骤 4：验证通过与 30 分钟 soak**

```bash
bash tools/unity-tests.sh EditMode Kcp
bash tools/unity-tests.sh PlayMode EchoSmokeTests
bash tools/kcp-soak.sh --minutes 30
```

预期：无任务/队列增长；无每 datagram MTU 数组和 64KiB writer 分配；默认仍是 WSS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/AOT/KcpNet Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTransportFactory.cs* \
  Assets/Scripts/Tests/Editor/Network Assets/Scripts/AOT/KcpNet/README.md tools/kcp-soak.sh
git commit -m "fix(kcp): bound client transport lifecycle"
```

---

### 任务 14：执行 P0 Gate 并发布验收报告

**优先级：** P0 收尾；依赖任务 1–12，任务 13 至少保证默认禁用

**文件：**
- 创建：`production/quality/p0-gate-2026-11.md`
- 修改：所有触及模块 README 的最终状态段

- [ ] **步骤 1：运行自动门禁**

```bash
bash tools/quality-gate.sh --p0
dotnet run -c Release --project tools/ServerLoadHarness -- --profile p0-flood --duration 600
```

预期：服务端/Unity 全量绿色、PlayMode 非 0、洪泛下队列有界且 tick 推进。

- [ ] **步骤 2：运行跨端恶意输入验收**

逐项记录：重复登录、换身份、未知 Zone、倒序/重复/回绕 sequence、伪造 timestamp、NaN/Infinity、异常 Y、永久 defend、WS 永不结束分片、慢发送端、UDP 垃圾包、客户端 10,000 条消息洪泛。

- [ ] **步骤 3：确认 P0 不变量**

报告必须列出精确测试总数、构建目标、队列峰值、tick p95/p99、working set、未执行项和证据路径。任何未执行项保持 Gate 为未通过，不能写“预计可用”。

- [ ] **步骤 4：Commit**

```bash
git add production/quality/p0-gate-2026-11.md \
  Assets/Scripts/Hotfix/GameSystems/*/README.md Assets/Scripts/AOT/*/README.md Server/README.md
git commit -m "docs: record p0 quality gate evidence"
```

---

### 任务 15：建立 WorldInstance 核心与 Global 适配层

**优先级：** P1；依赖 P0 Gate

**文件：**
- 创建：`Server/World/WorldInstanceId.cs`
- 创建：`Server/World/WorldCommand.cs`
- 创建：`Server/World/IWorldInstance.cs`
- 创建：`Server/World/WorldInstance.cs`
- 创建：`Server/World/WorldRegistry.cs`
- 创建：`Server/World/IWorldRouter.cs`
- 修改：`Server/Server3C.cs`
- 修改：`Server/Room/RoomManager.cs`
- 创建：`Server/Server.Tests/World/WorldInstanceLifecycleTests.cs`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的生命周期测试**

```csharp
Global_Mode_Routes_All_Authenticated_Sessions_To_One_Instance();
WorldRegistry_Enforces_Instance_And_Player_Limits();
WorldInstance_Owns_One_Bounded_Mailbox_And_Tick();
Empty_Instance_Is_Reclaimed_After_Configured_Timeout();
Destroying_Instance_Rejects_New_Commands_And_Closes_Members();
Transfer_Attach_Failure_Restores_Source_Instance();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~WorldInstanceLifecycleTests"`
预期：FAIL；World 类型不存在。

- [ ] **步骤 3：实现 Global-first 世界所有权**

```csharp
public interface IWorldInstance
{
    WorldInstanceId Id { get; }
    IngressResult TryEnqueue(WorldCommand command);
    WorldTransferSnapshot Detach(long characterId);
    WorldAttachResult Attach(WorldTransferSnapshot snapshot, IServerSession session);
}

public interface IWorldRouter
{
    IWorldInstance Resolve(LoginIdentity identity);
}
```

先只创建 `Global` 实例并把任务 5 的单世界行为放入该实例，不立刻开放第二实例。`Server3C` 降为 transport/auth/registry 组合根，不能直接改世界状态。换区使用 detach snapshot -> target attach -> 失败恢复源实例。

- [ ] **步骤 4：验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~WorldInstanceLifecycleTests"
bash tools/test-server-inprocess.sh
```

预期：World 生命周期测试与服务端全量通过，Global 行为无差分。

- [ ] **步骤 5：Commit**

```bash
git add Server/World Server/Server3C.cs Server/Room/RoomManager.cs \
  Server/Server.Tests/World/WorldInstanceLifecycleTests.cs Server/README.md
git commit -m "refactor(server): introduce global world instance ownership"
```

---

### 任务 16：迁移 AOI/Combat/Monster/Navigation/ORCA 并证明跨世界隔离

**优先级：** P1；依赖任务 15

**文件：**
- 修改：`Server/Entity/EntityStore.cs`
- 修改：`Server/Aoi/AoiBroadcaster.cs`
- 修改：`Server/Handler/CastHandler.cs`
- 修改：`Server/Monster/MonsterModule.cs`
- 修改：`Server/Movement/AvoidanceSystem.cs`
- 修改：`Server/Navigation/NavigationService.cs`
- 修改：`Server/World/WorldInstance.cs`
- 创建：`Server/Server.Tests/World/WorldIsolationTests.cs`
- 修改：`Server/Aoi/README.md`（若不存在则在 `Server/README.md` 记录）
- 修改：`Server/Monster/README.md`
- 修改：`Server/Movement/README.md`
- 修改：`Server/Navigation/README.md`

- [ ] **步骤 1：编写失败的跨世界测试**

```csharp
Same_Coordinates_Different_Worlds_Are_Not_Aoi_Visible();
Cast_Cannot_Target_Entity_In_Another_World();
Monster_Cannot_Acquire_Threat_Across_Worlds();
Orca_Neighbor_Set_Excludes_Other_World();
Navigation_Map_Is_Selected_By_WorldInstance();
Damage_Broadcast_Has_No_Recipient_From_Other_World();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~WorldIsolationTests"`
预期：FAIL；系统仍可访问全局 store。

- [ ] **步骤 3：迁移所有依赖到实例内构造**

`WorldInstance` 构造时创建并独占 EntityStore/AOI/CastHandler/Monster/Avoidance/Navigation。删除这些系统对 `Server3C` 全局查询的依赖；session 只携带 world route，不携带可变世界对象。加入第二个测试实例后运行隔离断言。

- [ ] **步骤 4：验证通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~WorldIsolationTests|FullyQualifiedName~Aoi|FullyQualifiedName~Combat|FullyQualifiedName~Monster|FullyQualifiedName~Avoidance"
```

预期：跨实例结果全为 0，现有单实例测试行为不变。

- [ ] **步骤 5：Commit**

```bash
git add Server/Entity Server/Aoi Server/Handler/CastHandler.cs Server/Monster \
  Server/Movement Server/Navigation Server/World Server/Server.Tests/World Server/README.md
git commit -m "refactor(server): isolate simulation systems per world"
```

---

### 任务 17：把 AOI 改为增量空间索引

**优先级：** P1；依赖任务 16

**文件：**
- 创建：`Server/Aoi/IAoiIndex.cs`
- 创建：`Server/Aoi/SpatialIndex.cs`
- 创建：`Server/Aoi/VisibilityIndex.cs`
- 修改：`Server/Aoi/AoiGrid.cs`
- 修改：`Server/Aoi/AoiBroadcaster.cs`
- 修改：`Server/Aoi/AoiBandScheduler.cs`
- 修改：`Server/Handler/CastHandler.cs`
- 创建：`Server/Server.Tests/Aoi/SpatialIndexTests.cs`
- 创建：`Server/Server.Tests/Aoi/AoiAllocationTests.cs`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的索引与分配测试**

```csharp
Moving_Across_Cell_Updates_Index_Exactly_Once();
Sight_Hysteresis_Preserves_Existing_Enter_Exit_Radii();
Reverse_Observer_Index_Tracks_Enter_Leave_Remove();
Removed_Entity_Leaves_No_Ghost_Visibility();
Query_Output_Is_Deterministic_By_EntityId();
Steady_State_Broadcast_Stays_Within_Allocation_Budget();
Index_Never_Returns_Another_World();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~SpatialIndexTests|FullyQualifiedName~AoiAllocationTests"`
预期：FAIL；索引不存在，旧实现全扫。

- [ ] **步骤 3：实现双向增量索引**

```csharp
public interface IAoiIndex
{
    void Upsert(long entityId, Vector3 position);
    void Remove(long entityId);
    void QueryVisible(long observerId, List<long> reusableOutput);
    IReadOnlyCollection<long> GetObserversOf(long entityId);
}
```

维护 cell->entities、entity->cell、observer->visible、entity->observers；输出按 entityId 稳定排序。复用 caller-owned buffer，不在 observer/tick 热路径创建多层 List/HashSet。保留既有迟滞半径与分带语义。

- [ ] **步骤 4：差分与压力验证**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Aoi"
dotnet run -c Release --project tools/ServerLoadHarness -- --profile aoi-200 --duration 600
```

预期：录制输入下 legacy/new 可见性一致；反向查询不再 O(N²)。

- [ ] **步骤 5：Commit**

```bash
git add Server/Aoi Server/Handler/CastHandler.cs Server/Server.Tests/Aoi Server/README.md
git commit -m "perf(aoi): add incremental spatial visibility index"
```

---

### 任务 18：共享空间邻居、约束 ORCA 并调度导航

**优先级：** P1；依赖任务 17

**文件：**
- 创建：`Server/Movement/IAgentNeighborProvider.cs`
- 创建：`Server/Movement/SpatialNeighborProvider.cs`
- 创建：`Server/Movement/IMotionConstraint.cs`
- 创建：`Server/Movement/NavigationMotionConstraint.cs`
- 修改：`Server/Movement/AvoidanceSystem.cs`
- 修改：`Server/Monster/SimpleMoveSystem.cs`
- 创建：`Server/Navigation/INavigationScheduler.cs`
- 创建：`Server/Navigation/NavigationScheduler.cs`
- 创建：`Server/Navigation/NavQueryRequest.cs`
- 创建：`Server/Navigation/NavQueryResult.cs`
- 创建：`Server/Navigation/NavPathfinderWorkspace.cs`
- 修改：`Server/Navigation/NavMeshPathfinder.cs`
- 修改：`Server/Monster/TargetDetector.cs`
- 创建：`Server/Server.Tests/Movement/HighDensityAvoidanceTests.cs`
- 创建：`Server/Server.Tests/Navigation/NavigationSchedulerTests.cs`
- 创建：`Server/Server.Tests/Monster/MonsterPerceptionBudgetTests.cs`
- 修改：`tools/ServerLoadHarness/Program.cs`
- 创建：`tools/ServerLoadHarness/Profiles/p1-reference.json`
- 创建：`tools/ServerLoadHarness/PerfEnvironmentGuard.cs`
- 创建：`Server/Server.Tests/Performance/ServerLoadProfileContractTests.cs`
- 修改：`Server/Movement/README.md`
- 修改：`Server/Navigation/README.md`
- 修改：`Server/Monster/README.md`

- [ ] **步骤 1：编写失败的规模化测试**

```csharp
Nearest_128_Are_Selected_By_Distance_Then_Id_From_200();
Orca_Final_Step_Cannot_Leave_Navmesh();
Five_Hundred_Agents_Meet_Neighbor_Budget();
Path_Query_Does_Not_Block_World_Tick();
Newer_Query_Generation_Discards_Stale_Result();
Duplicate_Quantized_Query_Is_Coalesced();
Workspace_Is_Reused_And_Queue_Is_Bounded();
Monster_Perception_Uses_Spatial_Query_And_Tick_Budget();
P1_Reference_Profile_Has_Exact_Fixed_Workload();
Official_Result_Requires_CoolPerf_Baseline_Environment();
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~HighDensityAvoidanceTests|FullyQualifiedName~NavigationSchedulerTests|FullyQualifiedName~MonsterPerceptionBudgetTests|FullyQualifiedName~ServerLoadProfileContractTests"
```

预期：FAIL；邻居按 ID 截断、导航同步阻塞、感知全扫。

- [ ] **步骤 3：实现共享索引与有界调度**

ORCA 从任务 17 的空间索引取邻居，超过 K 按 `(distanceSquared, entityId)` 最近优先；最终 step 经 navmesh motion constraint。导航请求含 `NpcId/Generation/Start/Goal/Radius/Priority`，结果只在 WorldInstance 线程应用；后台 worker 各自拥有 workspace。Monster idle 感知分桶且受每 tick AI 查询预算控制。

- [ ] **步骤 4：验证通过与基准**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Orca|FullyQualifiedName~Avoidance|FullyQualifiedName~Navigation|FullyQualifiedName~MonsterPerception"
dotnet run -c Release --project tools/ServerLoadHarness -- --profile p1-reference --runner-name cool-perf --warmup 120 --duration 600 --bots 200 --monsters 50 --clients 50 --position-hz 15 --cast-hz 2 --require-baseline-env
```

`p1-reference.json` 固定 Windows、.NET 8、至少 8 个逻辑核心和 16GB 内存；2 分钟预热、10 分钟采样；200 bots、50 monsters、50 simulated clients，每客户端 15Hz 位置与 2Hz 施法。只有名为 `cool-perf` 且通过硬件/运行时校验的专用 runner 能产出正式 Gate 结果；其他环境只能标记 trend/not-verified。正式阈值为 tick p95 < 50ms、p99 < 66ms，且 queue depth、working set 无单调增长。

- [ ] **步骤 5：Commit**

```bash
git add Server/Movement Server/Navigation Server/Monster Server/Server.Tests/Movement \
  Server/Server.Tests/Navigation Server/Server.Tests/Monster \
  Server/Server.Tests/Performance tools/ServerLoadHarness
git commit -m "perf(server): bound spatial movement and navigation work"
```

---

### 任务 19：迁移 Prefab 并删除遗留 Combat 双管线

**优先级：** P1；依赖任务 3

**文件：**
- 修改：`Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/TurtleShellPBR.prefab`
- 修改：`Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/SlimePBR.prefab`
- 修改：`Assets/Monstor/fantasySpider/SpiderCombat.prefab`
- 修改：`Assets/Monstor/DogKnight/Prefab/DogKnightCombat.prefab`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/DamageRequest.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IDamageable.cs`
- 删除：`Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitbox.cs*`
- 删除：`Assets/Scripts/Hotfix/GameSystems/Combat/HitZone.cs*`
- 删除：`Assets/Scripts/Hotfix/GameSystems/Combat/PlayerHitZone.cs*`
- 创建：`Assets/Scripts/Tests/Editor/Combat/LegacyCombatPrefabReferenceTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Combat/CombatPipelineConvergenceTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Combat/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/README.md`

- [ ] **步骤 1：先写资产引用与唯一管线测试**

```csharp
Known_Monster_Prefabs_Have_No_Legacy_Combat_Component();
Project_Assets_Have_No_Legacy_Script_Guid_Reference();
MonsterEntity_Has_No_Legacy_Combat_Component_Field();
Canonical_Damage_Path_Uses_DamageRequest();
```

- [ ] **步骤 2：运行测试确认当前资产引用被发现**

运行：`bash tools/unity-tests.sh EditMode LegacyCombatPrefabReferenceTests`
预期：FAIL，并精确列出四个已知 Prefab。

- [ ] **步骤 3：先迁移资产引用并提交可回滚检查点**

先从四个 Prefab 与 `MonsterEntity` 移除 legacy 组件/字段，保留脚本；运行 Prefab reference test 与 `MonsterWorldTests`，确认无 Missing Script 后建立第一提交：

```bash
git add Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/TurtleShellPBR.prefab \
  Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/SlimePBR.prefab \
  Assets/Monstor/fantasySpider/SpiderCombat.prefab \
  Assets/Monstor/DogKnight/Prefab/DogKnightCombat.prefab \
  Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs \
  Assets/Scripts/Hotfix/GameSystems/Monster/README.md
git commit -m "refactor(combat): detach legacy hit components"
```

- [ ] **步骤 4：验证通过**

把任务 3 的 `SkillDamageContext` 转换为唯一 `DamageRequest` 输入并迁移 `IDamageable` 契约；确认全项目 legacy script GUID 引用为 0 后，删除三个脚本及其 `.meta`，再运行：

```bash
bash tools/unity-tests.sh EditMode CombatPipelineConvergenceTests
bash tools/unity-tests.sh PlayMode MonsterWorldTests
bash tools/unity-tests.sh PlayMode HitJudgmentAlignmentTests
```

预期：无 Missing Script；技能 shape -> DamageRequest -> IDamageable 是唯一客户端伤害输入。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/DamageRequest.cs* \
  Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IDamageable.cs \
  Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitbox.cs* \
  Assets/Scripts/Hotfix/GameSystems/Combat/HitZone.cs* \
  Assets/Scripts/Hotfix/GameSystems/Combat/PlayerHitZone.cs* \
  Assets/Scripts/Tests/Editor/Combat/LegacyCombatPrefabReferenceTests.cs* \
  Assets/Scripts/Tests/Editor/Combat/CombatPipelineConvergenceTests.cs* \
  Assets/Scripts/Hotfix/GameSystems/Combat/README.md
git commit -m "refactor(combat): remove legacy hit pipeline"
```

---

### 任务 20：让 StateCoordinator 类型安全并修复 Sys3CEntry 初始化

**优先级：** P1；依赖任务 4、19

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/IFSM.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/IHitFSM.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/IStateCoordinator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StateCoordinator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/BaseFSM.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntryDependencies.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 修改：`Assets/Prefabs/Character/FemalePlayer.prefab`
- 修改：`Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab`
- 创建：`Assets/Scripts/Tests/Editor/Sys3C/StateCoordinatorTypeSafetyTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/Sys3CEntryInitializationTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-CurrentDesign.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`

- [ ] **步骤 1：编写失败的类型/初始化测试**

```csharp
StateCoordinator_Contains_No_Object_Reflection_Or_Magic_State_Int();
HandleDeath_Uses_Typed_Death_State();
Missing_Dependency_Disables_Entry_Before_Global_Registration();
Valid_Dependencies_Register_Exactly_Once();
Destroy_Before_Hud_Ready_Cancels_Late_Bind();
Player_Prefabs_Have_All_Required_Serialized_References();
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode StateCoordinatorTypeSafetyTests
bash tools/unity-tests.sh PlayMode Sys3CEntryInitializationTests
```

预期：FAIL；StateCoordinator 使用 object/reflection，初始化先注册后校验。

- [ ] **步骤 3：实现类型接口和初始化顺序**

`StateCoordinator(IBaseFSM baseFsm, IHitFSM hitFsm)`；删除空 `Initialize/Update`，直接使用 BaseState/HitState enum。`Sys3CEntryDependencies.IsValid(out error)` 先验证序列化依赖，再构建、订阅、注册，最后置 Ready；失败时 `enabled=false` 且不留注册。HUD 绑定使用 destroy CancellationToken。文档删除 AttackFSM 现存描述。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode StateCoordinatorTypeSafetyTests
bash tools/unity-tests.sh EditMode Sys3C
bash tools/unity-tests.sh PlayMode Sys3CEntryInitializationTests
bash tools/unity-tests.sh PlayMode PlayerDeathRespawnTests
```

预期：类型/生命周期测试和死亡复活回归全过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C Assets/Prefabs/Character/FemalePlayer.prefab \
  Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab Assets/Scripts/Tests/Editor/Sys3C \
  Assets/Scripts/Tests/PlayMode/Sys3CEntryInitializationTests.cs*
git commit -m "refactor(sys3c): type state coordination and safe initialization"
```

---

### 任务 21：让 ResourceManager 支持租约、并发单飞与失败释放

**优先级：** P1

**文件：**
- 创建：`Assets/Scripts/AOT/Core/Resource/AssetLease.cs`
- 创建：`Assets/Scripts/AOT/Core/Resource/SceneLease.cs`
- 创建：`Assets/Scripts/AOT/Core/Resource/IResourceBackend.cs`
- 创建：`Assets/Scripts/AOT/Core/Resource/AddressablesResourceBackend.cs`
- 创建：`Assets/Scripts/AOT/Core/Resource/PendingLoad.cs`
- 修改：`Assets/Scripts/AOT/Core/Resource/ResourceManager.cs`
- 修改：`Assets/Scripts/AOT/Core/Resource/HandleEntry.cs`
- 修改：`Assets/Scripts/AOT/Core/Resource/Res.cs`
- 修改：`Assets/Scripts/AOT/Core/Resource/ResourceLoadException.cs`
- 创建：`Assets/Scripts/Tests/Editor/Resource/ResourceManagerTests.cs`
- 修改：`Assets/Scripts/AOT/Core/Resource/README.md`

- [ ] **步骤 1：编写失败的生命周期测试**

```csharp
Concurrent_Acquire_Same_Key_Starts_One_Backend_Load();
Two_Leases_Release_Handle_After_Both_Dispose();
Lease_Dispose_Is_Idempotent();
Cancelled_Waiter_Does_Not_Increment_RefCount_Or_Cancel_Others();
Failed_Load_Releases_Exactly_Once_And_Allows_Retry();
Incompatible_Type_Fails_Before_RefIncrement();
Duplicate_Scene_Request_Does_Not_Start_Second_Load();
Scene_Failure_And_Cancel_Release_Handle();
Shutdown_Reports_Leaked_Leases();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode ResourceManagerTests`
预期：FAIL；lease/backend/pending 不存在。

- [ ] **步骤 3：实现主 API**

```csharp
public static UniTask<AssetLease<T>> AcquireAsync<T>(
    string key, CancellationToken cancellationToken = default) where T : UnityEngine.Object;

public static UniTask<SceneLease> AcquireSceneAsync(
    string key, LoadSceneMode mode, CancellationToken cancellationToken = default);
```

`AssetLease<T>`/`SceneLease` 使用 sealed class，Dispose 幂等。同 `(key,type)` 共享一个 pending operation，每位成功调用者获得独立 lease；取消等待者不增引用也不取消其他等待者。旧 `Load/Release(key)` 标记 `[Obsolete]`，作为迁移桥保留一轮。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode ResourceManagerTests
bash tools/unity-tests.sh EditMode
```

预期：资源失败/取消/重试全部释放恰好一次，EditMode 全量通过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/AOT/Core/Resource Assets/Scripts/Tests/Editor/Resource/ResourceManagerTests.cs*
git commit -m "feat(resource): add addressable leases and single-flight loading"
```

---

### 任务 22：初始化 Addressables 并迁移内建 UI

**优先级：** P1；依赖任务 21

**文件/资产：**
- 创建：`Assets/AddressableAssetsData/`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanelDescriptor.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/IUIPanelLoader.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/AddressablePanelLoader.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIBootstrap.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Const/UIConst.cs`
- 修改：`Assets/Scripts/AOT/DataDefinition/GameConsts.cs`
- 移动并保留 `.meta`：`Assets/Resources/Prefabs/UI/*.prefab` 到 `Assets/Addressables/UI/`
- 创建：`Assets/Addressables/UI/Descriptors/*.asset`
- 创建：`Assets/Scripts/Tests/Editor/Resource/AddressableCatalogContractTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/UI/UIManagerLoaderTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/AddressableUiLifecycleTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/README.md`
- 修改：`Assets/Scripts/AOT/Core/Resource/README.md`

- [ ] **步骤 1：编写失败的 catalog/loader 测试**

```csharp
Every_Panel_Key_Resolves_To_Typed_Descriptor();
No_Production_UI_Code_Uses_ResourcesLoad();
Load_Cancelled_Or_Failed_Does_Not_Register_Panel();
UiManager_Holds_Lease_Until_Destroy();
Repeated_Open_Close_Does_Not_Change_Lease_Count();
Addressables_Content_Build_Has_No_Duplicate_Address();
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode AddressableCatalogContractTests
bash tools/unity-tests.sh EditMode UIManagerLoaderTests
```

预期：FAIL；Addressables settings 不存在，UIManager 仍直接 Resources.Load。

- [ ] **步骤 3：用 Unity API 创建设置并迁移**

固定 addresses：`ui/player-hud`、`ui/target-panel`、`ui/character-select`、`ui/buff-icon`。加载 Addressable 的 `UIPanelDescriptor`，由 descriptor 持有 typed `UIPanel Prefab`；使用 `Object.Instantiate(descriptor.Prefab)`，不在 MonoBehaviour 中 GetComponent。loader 保存 panel->lease 映射，UIManager 销毁时释放。

- [ ] **步骤 4：验证通过与 content build**

```bash
bash tools/unity-tests.sh EditMode AddressableCatalogContractTests
bash tools/unity-tests.sh EditMode UIManagerLoaderTests
bash tools/unity-tests.sh PlayMode AddressableUiLifecycleTests
```

另执行 Addressables `BuildPlayerContent`；预期无重复 address、缺失依赖或 Resources fallback。

- [ ] **步骤 5：Commit**

```bash
git add Assets/AddressableAssetsData Assets/Addressables Assets/Resources/Prefabs/UI \
  Assets/Scripts/Hotfix/GameSystems/UI Assets/Scripts/AOT/DataDefinition/GameConsts.cs \
  Assets/Scripts/Tests/Editor/Resource Assets/Scripts/Tests/Editor/UI \
  Assets/Scripts/Tests/PlayMode/AddressableUiLifecycleTests.cs* Assets/Scripts/AOT/Core/Resource/README.md
git commit -m "feat(ui): load built-in panels through addressables"
```

---

### 任务 23：消除 UI 显隐竞态并补移动端基础适配

**优先级：** P1；依赖任务 22

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/PanelTransitionController.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/SafeAreaAdapter.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIBackStack.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/IUiLoadFailurePresenter.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/BuiltinUiLoadFailurePresenter.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Localization/ILocalizationService.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Accessibility/IAccessibilitySettings.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIBootstrap.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/TargetPanel.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/VirtualJoystick.cs`
- 创建：`Assets/Scripts/Tests/Editor/UI/UIPanelTransitionRaceTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/UI/UIBackStackTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/UI/UIContractTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/UISafeAreaTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/UI/UILoadFailureStateTests.cs`
- 创建：`Assets/Prefabs/UI/BuiltinUiLoadFailurePresenter.prefab`
- 修改：`Assets/Scenes/CharacterSelect.unity`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/README.md`

- [ ] **步骤 1：编写失败的竞态/输入测试**

```csharp
Show_Then_Hide_Before_Tween_Ends_FinalState_Is_Hidden();
Hide_Then_Show_Before_Tween_Ends_FinalState_Is_Visible();
Hidden_CanvasGroup_Is_Not_Interactable_Or_Raycastable();
Destroy_During_Transition_Cancels_Late_Mutation();
TargetDeath_Hide_Is_Observed_And_OwnerCancelled();
VirtualJoystick_ZeroRadius_Does_Not_Produce_NaN();
SafeArea_Updates_On_Orientation_And_Resolution_Change();
Back_Closes_Topmost_Modal_Before_Underlying_Panel();
Back_On_Empty_Stack_Uses_Explicit_Platform_Exit_Policy();
Missing_Addressable_Shows_Retryable_Placeholder_Without_Registering_Panel();
CharacterSelect_CompositionRoot_Has_Concrete_LoadFailurePresenter();
Interactive_Controls_Expose_Localization_And_Accessibility_Keys();
TextScale_And_ReduceMotion_Are_Applied_Through_Settings_Contract();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode UIPanelTransitionRaceTests`
预期：FAIL；当前动画最后完成者可能覆盖最后请求。

- [ ] **步骤 3：实现最后请求获胜的状态机**

每 panel 维护 desired visibility 与 transition generation；新请求取消旧请求，所有异步绑定 owner token。隐藏同时设 alpha=0、blocksRaycasts=false、interactable=false。SafeAreaAdapter 只在 safe area/分辨率变化时更新，不逐帧重建。

`UIBackStack` 区分普通 panel 与 modal，Android 返回键/键盘 Escape 先关闭最上层 modal，再关闭可返回 panel；空栈行为由显式 platform exit policy 决定。Addressables 加载失败时不注册目标 panel，由 `BuiltinUiLoadFailurePresenter : MonoBehaviour, IUiLoadFailurePresenter` 显示可重试占位。`UIBootstrap` 使用可被 Unity 序列化的具体字段 `BuiltinUiLoadFailurePresenter`，再按接口传给 `UIManager`；`CharacterSelect.unity` 直接引用 `Assets/Prefabs/UI/BuiltinUiLoadFailurePresenter.prefab`，该 prefab 是场景直接依赖，不通过 Addressables/Resources 加载，因此失败页不会递归依赖同一条故障加载链。重试按钮只重新请求原 address，并受 owner cancellation 控制。建立 `ILocalizationService` 与 `IAccessibilitySettings`（text scale、reduce motion、accessibility label）接口及开发默认实现；本轮只加契约/门禁与现有内建 panel 接线，不承诺完成全部翻译或屏幕阅读器适配。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode UIPanelTransitionRaceTests
bash tools/unity-tests.sh EditMode UIBackStackTests
bash tools/unity-tests.sh EditMode UIContractTests
bash tools/unity-tests.sh PlayMode UISafeAreaTests
bash tools/unity-tests.sh PlayMode UILoadFailureStateTests
bash tools/unity-tests.sh PlayMode HudLifecycleTests
```

PlayMode 测试从真实 `CharacterSelect` 组合根注入一个不存在的 address，断言具体 Presenter 已接线、错误态可见、重试可触发且目标 panel 未注册。预期：并发显隐、销毁、横竖屏、加载失败与既有 HUD 生命周期全过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI Assets/Scripts/Tests/Editor/UI \
  Assets/Scripts/Tests/PlayMode/UISafeAreaTests.cs* \
  Assets/Scripts/Tests/PlayMode/UI/UILoadFailureStateTests.cs* \
  Assets/Prefabs/UI/BuiltinUiLoadFailurePresenter.prefab* \
  Assets/Scenes/CharacterSelect.unity
git commit -m "fix(ui): serialize panel transitions and safe-area updates"
```

---

### 任务 24：优化 Nameplate/FloatText 的移动端预算与池复用

**优先级：** P1；依赖任务 4

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Nameplate/PooledNameplateView.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateCullPlanner.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateRenderer.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextRenderer.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs`
- 创建：`Assets/Scripts/Tests/Editor/Nameplate/NameplateMobileBudgetTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Nameplate/FloatTextPoolReuseTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/README.md`

- [ ] **步骤 1：编写失败的零分配/复用测试**

```csharp
CullPlanner_After_Warmup_Allocates_Zero_Bytes();
CullPlanner_Uses_Squared_Distance_And_Valid_Fade_Range();
Unchanged_Visibility_Does_Not_Call_SetActive_Again();
Old_Merge_Record_Cannot_Mutate_Reused_FloatText_Lease();
Entity_Unregister_Removes_Block_Throttle_Entry();
NonInteractive_Display_Canvas_Has_No_GraphicRaycaster();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode NameplateMobileBudgetTests`
预期：FAIL；热路径逐帧分配并重复 SetActive。

- [ ] **步骤 3：实现缓存 view、generation lease 与分桶更新**

CullPlanner 使用 caller-owned buffers、`sqrMagnitude` 和可见状态缓存；PooledNameplateView 缓存组件引用。FloatText lease 带 generation，旧 merge 记录自动失效。移除无用 GraphicRaycaster，清理节流字典。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode NameplateMobileBudgetTests
bash tools/unity-tests.sh EditMode FloatTextPoolReuseTests
bash tools/unity-tests.sh PlayMode PlayerHudBindingTests
```

预期：预热后 cull/过期清理零分配，池复用不串实体。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate Assets/Scripts/Tests/Editor/Nameplate
git commit -m "perf(nameplate): bound mobile display updates and pooling"
```

---

### 任务 25：移除全局 TimeScale HitStop

**优先级：** P1；依赖任务 4

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/IHitStopController.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/HitStopManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackProfile.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/VFX/HitStopLifecycleTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/README.md`

- [ ] **步骤 1：编写失败的来源/恢复测试**

```csharp
Remote_Hit_Does_Not_Freeze_Animator_Or_Time();
Local_Hit_Does_Not_Change_Global_TimeScale();
Disable_During_Freeze_Restores_Source_And_Target_Speeds();
Overlapping_Stops_Old_Generation_Cannot_Restore_Early();
Destroyed_Target_Does_Not_Leak_Tracked_State();
Zero_Duration_Is_A_NoOp();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh PlayMode HitStopLifecycleTests`
预期：FAIL；当前远端事件可触发且修改 Time.timeScale。

- [ ] **步骤 3：实现局部 Animator/Playable 停顿**

`RequestLocalHitStop(Animator source, Animator target, float duration)` 只接受本地 source；保存原 speed，重叠使用 generation/deadline，unscaled time 恢复。Disable/Destroy 恢复全部跟踪对象；VFX 目录不得再写 `Time.timeScale`。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh PlayMode HitStopLifecycleTests
bash tools/unity-tests.sh PlayMode ThreeClientCombatConsistencyTests
```

预期：远端攻击不冻结本地，全局时间不变，多人表现回归通过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX Assets/Scripts/Tests/PlayMode/VFX/HitStopLifecycleTests.cs*
git commit -m "fix(vfx): replace global hit stop with local animator freeze"
```

---

### 任务 26：统一 VFX 对象池与场景生命周期

**优先级：** P1；依赖任务 21、25

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/Pooling/IVfxPool.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/Pooling/PrefabVfxPool.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/VFX/Pooling/VfxLease.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/HitParticleController.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/IceBurstVFX.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/IceDecalVFX.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/PlayerHealVFX.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackSystem.cs`
- 修改：`Assets/Scripts/AOT/DataDefinition/GameConsts.cs`
- 修改：`Assets/AddressableAssetsData/`（用 Unity API 更新 `VFX` group）
- 移动并保留 `.meta`：`Assets/VFX/HitSparkParticle.prefab` 到 `Assets/Addressables/VFX/HitSparkParticle.prefab`
- 移动并保留 `.meta`：`Assets/Prefabs/VFX/HealGreenParticles.prefab` 到 `Assets/Addressables/VFX/HealGreenParticles.prefab`
- 创建：`Assets/Scripts/Tests/Editor/VFX/VfxPoolLifecycleTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Resource/AddressableVfxCatalogTests.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/VFX/VfxPoolSceneLifecycleTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/VFX/README.md`

- [ ] **步骤 1：编写失败的池生命周期测试**

```csharp
Different_Prefabs_Never_Share_Pool_Entries();
DisposeOwner_Reclaims_Active_And_Idle_Instances();
Return_Twice_Is_Idempotent();
IceDecal_Without_Renderer_Still_Returns();
Repeated_Heal_Reuses_Instance_After_Warmup();
Pool_Does_Not_Overwrite_Active_Lease();
HighFrequency_Vfx_Addresses_Resolve_Without_Resources_Fallback();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode VfxPoolLifecycleTests` 和 `bash tools/unity-tests.sh EditMode AddressableVfxCatalogTests`
预期：FAIL；静态池绑定首 prefab，活动对象未统一追踪。

- [ ] **步骤 3：实现按 prefab key 的 owner 池**

`IVfxPool.Rent(prefab, position, rotation, owner)` 返回幂等 `VfxLease`；每个 prefab 独立 bucket，owner 销毁回收 active/idle。PlayerHeal 与高频 hit/decal 迁移统一池；资源 prefab 通过任务 21 lease 持有，并加入 `VFX` Addressables group，address 由 `GameConsts` 集中定义。测试同时断言 catalog 中不存在重复 address 或落回 `Resources.Load` 的 VFX 路径。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode VfxPoolLifecycleTests
bash tools/unity-tests.sh EditMode AddressableVfxCatalogTests
bash tools/unity-tests.sh PlayMode VfxPoolSceneLifecycleTests
bash tools/unity-tests.sh PlayMode PlayerHealVFXTests
```

预期：场景退出无活动对象/资源 lease 泄漏，预热后无重复 Instantiate/Destroy。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX Assets/Scripts/Tests/Editor/VFX \
  Assets/Scripts/Tests/Editor/Resource/AddressableVfxCatalogTests.cs* \
  Assets/Scripts/Tests/PlayMode/VFX/VfxPoolSceneLifecycleTests.cs* \
  Assets/Scripts/AOT/DataDefinition/GameConsts.cs Assets/AddressableAssetsData \
  Assets/VFX/HitSparkParticle.prefab* Assets/Prefabs/VFX/HealGreenParticles.prefab* \
  Assets/Addressables/VFX
git commit -m "perf(vfx): centralize prefab-keyed effect pooling"
```

---

### 任务 27：给客户端 Monster 感知建立分桶预算

**优先级：** P1

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Monster/AI/ITargetQuery.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Monster/AI/PhysicsTargetQuery.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Monster/AI/MonsterPerceptionScheduler.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Monster/MonsterPerformanceSettings.cs`
- 创建：`Assets/PreRes/Settings/Monster/MonsterPerformanceSettings.asset`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIBrain.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/PhysicsRegistry.cs`
- 创建：`Assets/Scripts/Tests/Editor/Monster/PerceptionBudgetTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/README.md`

- [ ] **步骤 1：编写失败的预算测试**

```csharp
FindNearbyNonAlloc_Reuses_Caller_Buffer();
NoTarget_Performs_At_Most_Five_Queries_Per_Second();
Scheduler_Never_Exceeds_Eight_Queries_Per_Frame();
Existing_Valid_Target_Does_Not_Rescan_Every_Frame();
Warm_Query_Path_Allocates_Zero_Bytes();
Online_Npc_View_Does_Not_Run_Local_Ai();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode PerceptionBudgetTests`
预期：FAIL；当前无目标怪每帧空间查询并分配。

- [ ] **步骤 3：实现 NonAlloc query 与 round-robin scheduler**

默认无目标感知 5Hz，每帧最多 8 次空间查询；已有目标只做无分配有效性检查。`ITargetQuery.FindNearbyNonAlloc(position, radius, List<ITargetable> results)` 复用 caller buffer。所有数值来自 `MonsterPerformanceSettings`。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode PerceptionBudgetTests
bash tools/unity-tests.sh PlayMode MonsterWorldTests
```

预期：预热后热路径零 GC，在线 NPC 不运行本地 AI。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/PhysicsRegistry.cs \
  Assets/PreRes/Settings/Monster Assets/Scripts/Tests/Editor/Monster/PerceptionBudgetTests.cs*
git commit -m "perf(monster): budget local perception queries"
```

---

### 任务 28：实现 Monster 真对象池与完整复位协议

**优先级：** P1；依赖任务 19、27

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/Monster/Pooling/IMonsterPool.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Monster/Pooling/MonsterPool.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Monster/Pooling/DestroyingMonsterPool.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/MonsterSpawner.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIBrain.cs`
- 创建：`Assets/Scripts/Tests/PlayMode/Monster/MonsterPoolLifecycleTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/README.md`

- [ ] **步骤 1：编写失败的复位测试**

```csharp
Death_Returns_Entity_To_Pool_Instead_Of_Destroying();
Rent_After_Death_Reenables_NavAgent_And_Collider();
Rent_After_Death_Has_Fresh_Stats_Ai_Damage_And_Effects();
Repeated_Rent_Does_Not_Duplicate_Event_Handlers();
Despawn_Unregisters_Physics_And_Perception();
Respawn_Update_Uses_Reusable_Buffer();
SetActive_True_Happens_After_ResetForSpawn();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh PlayMode MonsterPoolLifecycleTests`
预期：FAIL；现有 pool 只出不进，死亡 Destroy。

- [ ] **步骤 3：实现显式复位合同**

```csharp
public void ResetForSpawn(MonsterConfig config, Vector3 spawnPoint);
public void ResetForDespawn();
```

对象保持 inactive 完成全部复位后再 SetActive(true)。复位 NavAgent、Animator、Stats、AIContext、DamagePipeline、collider、target、状态效果、订阅与注册。`DestroyingMonsterPool` 作为可配置回滚实现，不绕过接口。

- [ ] **步骤 4：验证通过与 100 次复用**

```bash
bash tools/unity-tests.sh PlayMode MonsterPoolLifecycleTests
bash tools/unity-tests.sh PlayMode MonsterWorldTests
```

预期：同一实体 100 次 rent/return 后订阅、注册和活动对象数量为常数。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster Assets/Scripts/Tests/PlayMode/Monster
git commit -m "perf(monster): implement reset-safe entity pooling"
```

---

### 任务 29：建立 Android/iOS 可重复构建入口

**优先级：** P1；依赖任务 22

**文件：**
- 创建：`Assets/Scripts/Editor/Build/BuildScenes.cs`
- 创建：`Assets/Scripts/Editor/Build/MobileBuildSettings.cs`
- 创建：`Assets/Scripts/Editor/Build/AndroidBuildScript.cs`
- 创建：`Assets/Scripts/Editor/Build/IosBuildScript.cs`
- 修改：`Assets/Scripts/Editor/BuildScript.cs`
- 修改：`Assets/Scripts/Editor/WebGLBuildScript.cs`
- 修改：`ProjectSettings/ProjectSettings.asset`
- 创建：`Assets/Scripts/Tests/Editor/Build/MobileBuildSettingsTests.cs`
- 创建：`docs/release/mobile-build.md`

- [ ] **步骤 1：编写失败的构建合同测试**

```csharp
Release_Rejects_DefaultCompany_And_Default_ApplicationId();
Release_Requires_NonPlaceholder_Version();
Android_Release_Requires_Il2Cpp_Arm64_And_Aab();
Ios_Export_Requires_Il2Cpp_And_Arm64();
All_Targets_Use_One_Explicit_Scene_List();
Build_Settings_Are_Restored_After_Failure();
Addressables_Content_Is_Built_Before_Player();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode MobileBuildSettingsTests`
预期：FAIL；移动构建入口不存在，项目仍是默认身份。

- [ ] **步骤 3：实现统一构建设置**

Android Release：IL2CPP/ARM64/AAB；Development 可显式 APK。iOS：IL2CPP/ARM64，导出 Xcode 到 `Build/iOS`，签名留 CI/Xcode。company/app id/version/keystore 从环境读取；Development 缺省 application id 为 `com.jincaizi.cool.dev`，商店/Release 构建不得使用该值；Release 检测 `DefaultCompany`、Unity 默认 id、开发 id 或缺失身份立即失败。所有临时 PlayerSettings 在 finally 还原；日志写 Unity、Git commit、ProtocolVersion、catalog hash，不写秘密。

- [ ] **步骤 4：验证构建**

```powershell
Unity.exe -batchmode -nographics -quit -projectPath E:\CodeForJob\Cool -executeMethod Cool.EditorTools.AndroidBuildScript.BuildDevelopment
Unity.exe -batchmode -nographics -quit -projectPath E:\CodeForJob\Cool -executeMethod Cool.EditorTools.IosBuildScript.ExportXcode
```

预期：Windows runner 生成 Android APK/AAB；iOS 仅在 macOS runner 报完整成功，其他环境明确“未执行”。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Editor/Build Assets/Scripts/Editor/BuildScript.cs \
  Assets/Scripts/Editor/WebGLBuildScript.cs Assets/Scripts/Tests/Editor/Build \
  ProjectSettings/ProjectSettings.asset docs/release/mobile-build.md
git commit -m "build: add reproducible android and ios pipelines"
```

---

### 任务 30：落地架构守卫并低风险拆分 Bag/Stamina asmdef

**优先级：** P1 后段

**文件：**
- 创建：`Assets/Scripts/Tests/Editor/Architecture/MonoBehaviourLookupGuardTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Architecture/AsmdefDependencyGuardTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Architecture/GameplayResourcesLoadGuardTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Architecture/mono-behaviour-lookup-baseline.txt`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Bag/Hotfix.GameSystems.Bag.asmdef`
- 创建：`Assets/Scripts/Hotfix/GameSystems/Stamina/Hotfix.GameSystems.Stamina.asmdef`
- 修改：`Assets/Scripts/Hotfix/GameSystems/GameSys.asmdef`
- 修改：`Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`
- 修改：两个 PlayMode asmdef
- 修改：`Assets/Scripts/Hotfix/GameSystems/Bag/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Stamina/README.md`
- 修改：`docs/superpowers/specs/2026-05-06-gamesystems-assembly-split-design.md`（标记被本计划部分取代）

- [ ] **步骤 1：先写守卫测试与当前 baseline**

```csharp
No_New_Public_MonoBehaviour_Runtime_Lookup_Above_Baseline();
Removed_Source_Violation_Must_Be_Removed_From_Baseline();
No_Executable_ResourcesLoad_In_GameSystems();
Commented_Legacy_ResourcesLoad_Text_Does_Not_Trigger_Guard();
Bag_Assembly_Does_Not_Reference_GameSys();
Stamina_Assembly_Does_Not_Reference_GameSys();
GameSys_References_Bag_And_Stamina_Explicitly();
No_Document_Claims_HybridClr_Is_Installed();
```

- [ ] **步骤 2：运行守卫，确认 baseline 能抓新增违规**

运行：`bash tools/unity-tests.sh EditMode Architecture`
预期：首次按当前存量生成 baseline 后，通过注入一个临时 fixture 验证新增违规会失败，再删除 fixture。

- [ ] **步骤 3：拆分低依赖程序集**

先拆 Bag，再全量编译/测试；再拆 Stamina。新程序集不得反向引用 `GameSys`。更大范围 Sys3C/Skills/Combat/Monster 拆分登记 P2，不在本任务夹带。架构守卫只允许 MonoBehaviour lookup baseline 减少；P1 触及文件必须清零自身违规。

`GameplayResourcesLoadGuardTests` 扫描 `Assets/Scripts/Hotfix/GameSystems/**/*.cs` 的可执行调用并忽略注释/字符串，任何 `Resources.Load*` 调用都立即失败，不设置存量 baseline 或永久白名单。任务 22 必须已迁移当前 `UIManager` 调用；`ItemCell` 中仅有的注释文本由专门 fixture 证明不会误报。若执行时发现新的真实调用，必须在本任务内迁到任务 21 的 `ResourceLease` API，并同步所属模块 README，不能把它加入豁免表。

- [ ] **步骤 4：验证通过**

```bash
bash tools/unity-tests.sh EditMode Architecture
bash tools/unity-tests.sh EditMode
bash tools/unity-tests.sh PlayMode StaminaIntegrationTests
```

预期：Bag/Stamina 独立编译，所有引用显式，GameSystems 可执行代码中 `Resources.Load*` 为 0，且未重新引入 HybridCLR 承诺。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/Editor/Architecture Assets/Scripts/Hotfix/GameSystems/Bag \
  Assets/Scripts/Hotfix/GameSystems/Stamina Assets/Scripts/Hotfix/GameSystems/GameSys.asmdef \
  Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef Assets/Scripts/Tests/PlayMode \
  docs/superpowers/specs/2026-05-06-gamesystems-assembly-split-design.md
git commit -m "refactor: isolate bag and stamina assemblies with architecture guards"
```

---

### 任务 31：建立 GitHub CI、覆盖率基线与 0-tests 门禁

**优先级：** P1；依赖任务 1、29、30

**文件：**
- 创建：`.github/workflows/quality-gates.yml`
- 创建：`.github/workflows/mobile-build.yml`
- 创建：`tools/ci/run-unity-tests.ps1`
- 创建：`tools/ci/assert-test-results.ps1`
- 创建：`tools/ci/check-coverage.ps1`
- 创建：`tools/ci/fixtures/zero-tests.xml`
- 创建：`tools/ci/fixtures/passing.xml`
- 创建：`coverage-baseline.json`
- 修改：`Server/Server.Tests/Server.Tests.csproj`
- 修改：`Packages/manifest.json` 与 `Packages/packages-lock.json`（锁定 Unity Code Coverage 包）
- 创建：`docs/release/ci-required-checks.md`

- [ ] **步骤 1：给 CI 脚本写 fixture 自测**

断言测试 XML 中 0 tests、失败、崩溃、超时均返回非零；覆盖率任一目标程序集比 baseline 下降 >1 个百分点返回非零；首次失败即失败，诊断性重跑不能转绿。

- [ ] **步骤 2：本地运行脚本验证失败/成功 fixture**

```powershell
pwsh tools/ci/assert-test-results.ps1 -Fixture tools/ci/fixtures/zero-tests.xml
pwsh tools/ci/assert-test-results.ps1 -Fixture tools/ci/fixtures/passing.xml
```

预期：第一条非零、第二条 0。

- [ ] **步骤 3：创建工作流**

GitHub hosted runner 跑 Server build/test；Windows self-hosted Unity runner 跑 EditMode、通用 PlayMode、native PlayMode、Addressables content build、Android Development Build、架构守卫。macOS self-hosted runner 存在时导出 iOS；不存在则 workflow 状态明确为 skipped/not-verified，不计入 iOS 发布门禁。上传 XML、日志、coverage、构建产物。

- [ ] **步骤 4：生成并提交基线**

运行本地完整门禁生成 `coverage-baseline.json`。P1 只要求禁止下降；80%/70% 目标留 P2。检查 GitHub required checks 与分支保护配置文档一致。

- [ ] **步骤 5：Commit**

```bash
git add .github tools/ci coverage-baseline.json Server/Server.Tests/Server.Tests.csproj \
  Packages/manifest.json Packages/packages-lock.json docs/release/ci-required-checks.md
git commit -m "ci: enforce tests builds architecture and coverage baselines"
```

---

### 任务 32：加入 OpenTelemetry、健康检查与 WSS 部署基线

**优先级：** P1；依赖任务 9、10、29

**文件：**
- 创建：`Server/Diagnostics/Telemetry.cs`
- 创建：`Server/Diagnostics/HealthServer.cs`
- 修改：`Server/Diagnostics/ServerMetrics.cs`
- 修改：`Server/Server3C.cs`
- 修改：`Server/Program.cs`
- 修改：`Server/Server3C.csproj`
- 创建：`Server/Server.Tests/Diagnostics/TelemetryContractTests.cs`
- 创建：`Server/Server.Tests/Diagnostics/HealthServerTests.cs`
- 创建：`production/deploy/Caddyfile`
- 创建：`production/deploy/README.md`
- 修改：`Server/README.md`

- [ ] **步骤 1：编写失败的指标/健康测试**

```csharp
Metrics_Expose_Tick_Queue_Session_Backpressure_Aoi_Nav_And_Allocation();
Live_Is_Healthy_When_Process_Is_Running();
Ready_Fails_When_World_Tick_Is_Stalled_Or_Auth_Is_Misconfigured();
Management_Endpoint_Defaults_To_Loopback();
Shutdown_Stops_Admission_Drains_Bounded_Queues_And_Closes_Sessions();
Production_Profile_Requires_Wss_Proxy_And_Rejects_DevAuth();
```

- [ ] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Diagnostics"`
预期：FAIL；telemetry/health 不存在。

- [ ] **步骤 3：实现标准指标与部署样例**

使用 `System.Diagnostics.Metrics` + OpenTelemetry Prometheus exporter；管理端默认 `127.0.0.1`。`/health/live` 只表示进程，`/health/ready` 检查最近世界 tick、auth 配置与队列健康。Caddy 示例只向公网暴露 WSS，代理到私网 WS；指标/健康不公开。停机不声称持久化未存在的数据。

- [ ] **步骤 4：验证通过与停机演练**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Diagnostics"
dotnet run -c Release --project tools/ServerLoadHarness -- --profile graceful-shutdown
```

预期：指标可抓取、readiness 能发现 tick stall、停机无残留 listener/session/world task。

- [ ] **步骤 5：Commit**

```bash
git add Server/Diagnostics Server/Server3C.cs Server/Program.cs Server/Server3C.csproj \
  Server/Server.Tests/Diagnostics production/deploy Server/README.md
git commit -m "feat(ops): add telemetry health and wss deployment baseline"
```

---

### 任务 33：执行 P1 Gate 并形成发布准备结论

**优先级：** P1 收尾；依赖任务 15–32

**文件：**
- 创建：`production/quality/p1-gate-2026-11.md`
- 创建：`production/quality/mobile-profile-2026-11.md`
- 修改：`AGENTS.md` Current Status 与测试数（仅在实际结果确认后）
- 修改：相关模块 README 的已知限制与证据链接

- [ ] **步骤 1：运行完整自动门禁**

```bash
bash tools/quality-gate.sh --p1
dotnet run -c Release --project tools/ServerLoadHarness -- --profile p1-reference --runner-name cool-perf --warmup 120 --duration 600 --bots 200 --monsters 50 --clients 50 --position-hz 15 --cast-hz 2 --require-baseline-env
```

预期：所有 required checks 通过；专用 perf runner 达到 p95/p99；0 tests、skip 冒充通过或覆盖率下降均失败。

- [ ] **步骤 2：运行 30 分钟客户端 soak 与移动 profiling**

循环进入/退出 UI、世界、战斗和场景；记录 Addressables leases、池 active/idle、EventBus subscriptions、managed allocations、FPS/frame-time。Android 参考机要求 p95 frame <=33.3ms、稳态无每帧 GC；实际机型/OS/分辨率写入报告。

- [ ] **步骤 3：验证双世界与公网边界**

两个 WorldInstance 同坐标实体互不可见/不可命中/不可仇恨/不可 ORCA；生产 profile 仅 WSS、协议 v2、HMAC/外部票据 validator，v1/dev-auth/KCP 默认禁用。Caddy/WSS、health、metrics、优雅停机演练留证。

- [ ] **步骤 4：诚实记录未执行项**

iOS 若无 macOS runner，结论必须是“Android 公开测试准备；iOS 未验证”，不能写全移动平台通过。任何物理设备、TLS、压测或安全项未执行，都保持对应 Gate 未通过。

- [ ] **步骤 5：Commit**

```bash
git add production/quality AGENTS.md Assets/Scripts/Hotfix/GameSystems/*/README.md \
  Assets/Scripts/AOT/*/README.md Server/README.md
git commit -m "docs: record p1 release-readiness evidence"
```

---

## 自检

### 1. 规格覆盖度

| 规格区域 | 对应任务 |
|---|---|
| P0 测试可信 | 1 |
| Bag 数据正确性 | 2 |
| 不可变技能配置 / EventBus / 技能事务 | 3–4 |
| 单世界收口 / 登录幂等与认证 | 5–6 |
| 服务器权威移动 / 格挡精力 | 7–8 |
| GameLoop / admission / WS / KCP / 客户端主线程 | 9–13 |
| P0 验收 | 14 |
| WorldInstance 与跨世界隔离 | 15–16 |
| AOI / ORCA / 导航 / 服务端怪物规模 | 17–18 |
| Combat / 3C 收敛 | 19–20 |
| ResourceLease / Addressables / UI | 21–23 |
| Nameplate / VFX / 客户端 Monster | 24–28 |
| 移动构建 / 架构 / CI | 29–31 |
| 可观测性 / WSS / 停机 | 32 |
| P1 验收 | 33 |

规格中的目标、非目标、反目标、数值预算与 Gate 都有对应任务；P2 延期项没有混入实现任务。

### 2. 类型与接口一致性

- 登录：`LoginRequest.RequestId/AuthTicket/CharacterId` -> `ILoginTicketValidator` -> `LoginIdentity` -> `IWorldRouter`。
- 网络：`InboundEnvelope.ReceivedMonotonicTicks` 同时供 admission 与 `AuthoritativeMovementService`，不回退客户端墙钟。
- 移动：`SequenceMath.IsNewer` 是服务端和 Unity 测试共同真值；ack 始终回最后接受 sequence。
- 格挡：`GuardIntentRequest` 只表达意图；`GuardSystem/GuardStateResponse/EntitySnapshot` 才表达权威状态。
- 客户端：transport callback -> `ClientMessagePump` -> 主线程 `MessageDispatcher/IClientMessageBus`；CombatBridge 不越级订阅 adapter。
- 资源：`Res.AcquireAsync<T>` -> `AssetLease<T>`；UI/VFX 只持 lease，不再手工 key 配对。
- 世界：`LoginIdentity.AllowedWorldId` -> `WorldRouter` -> `WorldInstanceId`；任何模拟系统只拿实例内 store/index。

### 3. 审查重点回钉

| 审查重点 | 钉住它的测试/任务 |
|---|---|
| sequence 回绕/重放 | 任务 7 `SequenceMath_*`、`Duplicate_And_Older_*` |
| 可靠队列满不能静默丢 | 任务 9 `Reliable_Command_Overflow_*`、任务 10/12 overflow 测试 |
| Addressables 取消/失败泄漏 | 任务 21 ResourceManagerTests、任务 22 UI lifecycle |
| 重复登录/断线竞态 | 任务 6 LoginIdempotencyTests |
| 世界切换失败/跨世界串线 | 任务 15 transfer restore、任务 16 WorldIsolationTests |

### 4. 比例与无歧义检查

- 计划长于规格是因为覆盖 33 个可独立审查交付物；没有复制完整函数体，只固定实施者无法自行推断的签名、数值、断言、命令与失败策略。
- 每个任务都包含红灯、最小实现、绿灯和 commit；没有模糊占位步骤。
- 删除类任务（Combat）先迁资产再删脚本；涉及当前脏文件的任务要求精确暂存，避免覆盖用户改动。
- 外部条件无法满足时（macOS/iOS runner、物理 Android 机、WSS 代理），Gate 保持未通过并记录“未执行”，不得推测为成功。

## 执行建议

推荐使用 `subagent-driven-development`：33 个任务大多能按工作域独立验收，但身份/权威/网络边界的错误上线代价高；每个任务由全新实现者执行，紧接一个全新审查者，最后再做 P0/P1 两次全局审查。若更重视速度和成本，也可使用 `executing-plans` 在当前会话逐任务执行，并在 P0 Gate 与 P1 Gate 各安排一次独立总审查。

# DogKnight 与 SpiderBoss 接入刷怪列表 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把 DogKnight（精英）与 SpiderBoss（Boss）两个现成美术资源接入服务端刷怪列表，并让客户端按服务器时间轴渲染其模型与动画，做到多个客户端在同一时刻看到的位置、朝向、动作、状态一致。

**架构：** 服务端以模板驱动（配置 + 技能表 + 布局点位），并让快照发送调度把「状态翻转」当作变化信号立即发；客户端新增怪物专用动画驱动，状态来自快照缓冲在 `renderAt` 的采样（与位置插值同一根服务器时间轴），攻击动作由施法广播的时间戳排程并推导变体。控制器保持标准参数+转移图，状态机逻辑全在纯 C# 里。

**技术栈：** Unity 2022.3.25f1（UGUI / Animator）、.NET 8 服务端（xUnit）、KCP + protobuf、Unity MCP（`unity-mcp-cli`）。

**设计依据：** [docs/superpowers/specs/2026-10-12-dogknight-spiderboss-monsters-design.md](../specs/2026-10-12-dogknight-spiderboss-monsters-design.md)（下称 spec）。本计划与 spec 有两处**有意偏离**，已在对应任务里标注原因。

---

## 命令约定

在项目根目录 `E:/CodeForJob/Cool` 执行。

| 简写 | 实际命令 |
|---|---|
| `STEST <过滤>` | `dotnet test Server/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~<过滤>" --nologo -v q` |
| `UTEST <类名>` | `unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode","testClass":"<类名>"}'` |
| `UPLAY <类名>` | 同上，`"testMode":"PlayMode"`，长测试另加 `"timeout":600000` |
| `MCP <工具> <json>` | `unity-mcp-cli run-tool <工具> --path "E:/CodeForJob/Cool" --input '<json>'` |

**基线（开工前已实测）：** `STEST MonsterModuleTests` → 7 passed。
Unity EditMode 全量基线在任务 1 开工前用 `MCP tests-run '{"testMode":"EditMode"}'` 记一次（预期全绿），后续每个任务只跑相关类。

**Unity 测试注意**：`tests-run` 的 `testClass` 必须用**裸类名**（全限定名会返回 "No tests found"）；跑测试前所有打开的场景必须已保存，否则工具抛 `InvalidOperationException`。

---

## 文件结构

**服务端（新增/修改）**

| 文件 | 职责 |
|---|---|
| `Server/Config/monster_config.json` | 怪物模板表（新增 t3/t4） |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` | 双端共享技能规则表（新增 50003/50004） |
| `Server/Monster/MonsterModule.cs` | 模板→技能映射、默认布局 |
| `Server/Config/ConfigLoader.cs` | 配置缺失时的保底表 |
| `Server/Aoi/AoiBandScheduler.cs` | 发送节拍（新增「状态变化即发」） |
| `Server/Aoi/AoiBroadcaster.cs` | 调用点（透传 ActionState/Dead） |

**客户端（新增/修改）**

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteInterpolator.cs` | 快照缓冲：位置插值 **+ 状态阶梯采样** |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs` | **新增**：攻击排程队列 + 变体推导（纯 C#） |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs` | **新增**：怪物动画驱动（快照状态 + 攻击排程 → Animator 参数） |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs` | 视图创建（目录查表）/ 渲染时喂状态 / 攻击路由 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | `(templateId, prefab)` 表 + 广播路由 |
| `Assets/Monstor/DogKnight/Animator/NpcAnimator_DogKnight.controller` | **新增**：骑士状态机 |
| `Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller` | **新增**：Boss 状态机 |
| `Assets/Monstor/DogKnight/Prefab/DogKnightMonster.prefab` | **新增**：DogPBR 的 Variant + 驱动组件 |
| `Assets/Monstor/fantasySpider/SpiderMonster.prefab` | **新增**：Boss 预制体（缩放 ×2） |

---

## 任务 1：服务端怪物模板与技能表

**文件：**
- 修改：`Server/Config/monster_config.json`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs:50-62`
- 修改：`Server/Monster/MonsterModule.cs:39-44`（`SkillIdOf`）、`:57-79`（`SpawnDefaultLayout` 及注释）
- 修改：`Server/Config/ConfigLoader.cs`（`CreateDefaultConfig`）
- 测试：`Server/Server.Tests/Monster/MonsterModuleTests.cs`

- [ ] **步骤 1：写失败的测试**

在 `Server/Server.Tests/Monster/MonsterModuleTests.cs` 末尾追加（按需补 `using System.Linq;` / `using Cool.SyncCore;` / `using KcpServer.Config;`）：

```csharp
    [Fact]
    public void DefaultLayout_Carries_Six_Templates_In_Order()
    {
        var (store, _) = Create(6);

        var templates = store.All
            .Where(e => e.Kind == (byte)EntityKind.Npc)
            .OrderBy(e => e.PlayerId)
            .Select(e => e.TemplateId)
            .ToArray();

        Assert.Equal(new[] { 2, 1, 1, 2, 3, 4 }, templates);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void New_Templates_Can_Spawn(int templateId)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);

        var npcId = module.SpawnMonster(templateId, new Vector3(5f, 0f, 5f), new Vector3(5f, 0f, 5f));

        Assert.True(npcId > 0);
        var npc = store.GetPlayer(npcId);
        Assert.NotNull(npc);
        Assert.Equal(templateId, npc!.TemplateId);
    }

    /// <summary>防「静默冻结」：怪物施法形状范围取 Config.attackRange（MonsterBtActions），
    /// 超过表上限会被 CombatRules.ValidateShape 拒收，而拒收只写审计日志——怪物在攻击距离上零表现。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Template_AttackRange_Fits_Its_Combat_Rule(int templateId)
    {
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Config", "monster_config.json");
        var config = ConfigLoader.LoadMonsterTemplates(path)[templateId];
        var skillId = MonsterModule.SkillIdOf(templateId);

        Assert.True(CombatRules.TryGet(skillId, out var rule), $"template {templateId} 缺 {skillId} 表项");
        Assert.True(config.attackRange <= rule!.MaxRange + 0.01f,
            $"template {templateId} attackRange={config.attackRange} > skill {skillId} maxRange={rule.MaxRange}");
    }
```

- [ ] **步骤 2：运行测试验证失败**

运行：`STEST MonsterModuleTests`
预期：FAIL —— `New_Templates_Can_Spawn` 抛 `ArgumentException: Unknown monster template: 3`；`DefaultLayout_Carries_Six_Templates_In_Order` 同样抛；编译错误 `MonsterModule.SkillIdOf` 不可访问（`private`）。

- [ ] **步骤 3：`SkillIdOf` 提为 public 并补映射**

`Server/Monster/MonsterModule.cs:39`：

```csharp
        /// <summary>模板 → 战斗技能 id。**public 自 2026-10-12**：测试要断言
        /// 「配置 attackRange ≤ 表上限」（防静默冻结），需要拿到映射。</summary>
        public static int SkillIdOf(int templateId) => templateId switch
        {
            1 => 50001,
            2 => 50002,
            3 => 50003,   // DogKnight 精英
            4 => 50004,   // SpiderBoss
            _ => throw new ArgumentException($"No combat skill mapped for monster template {templateId}")
        };
```

- [ ] **步骤 4：布局尾部追加两个点位**

`Server/Monster/MonsterModule.cs:65` 的 `SpawnDefaultLayout`，注释同步成：

```csharp
        /// <summary>
        /// 固定布局生成（e2e 确定性依赖——T5 注释同步此表）：
        ///   [0] Wolf(t2)  (12,0,0)  面向原点——开局即感知原点观察者（检测 15m ≥ 12m）
        ///   [1] Slime(t1) (40,0,0)  巡逻可视（观察者视野 80m）、不感知（检测 8m）
        ///   [2] Slime(t1) (-40,0,0) 同 [1]
        ///   [3] Wolf(t2)  (0,0,40)  面向原点（观察者视野 80m 内，不感知——距离 40m > 15m）
        ///   [4] DogKnight(t3) 与 [5] SpiderBoss(t4)：**开局不得仇恨** —— 硬规则是
        ///       「到出生点的距离 > 感知半径 + 巡逻半径」（DogKnight 18+6=24m、SpiderBoss 25+12=37m）；
        ///       实现取的是带 4m 余量的 28m / 41m
        ///       ⚠️ 初稿把这两个点位按"到**原点**的距离"推导（(0,0,-20) / (-30,0,-30)）是错的：
        ///       DemoDay 的出生点是 (10, 0, -15.5)，DogKnight 距它只有 11m < 感知 18m，
        ///       实测 12 分钟内 8/8 次把挂机玩家打死。**原点只是 e2e 测试观察者的位置。**
        /// count 超过布局长度时循环复用；朝向一律面向原点（感知确定性）。
        /// </summary>
        public void SpawnDefaultLayout(int count)
        {
            var layout = new (int TemplateId, Vector3 Position)[]
            {
                (2, new Vector3(12f, 0f, 0f)),
                (1, new Vector3(40f, 0f, 0f)),
                (1, new Vector3(-40f, 0f, 0f)),
                (2, new Vector3(0f, 0f, 40f)),
                (3, new Vector3(-20f, 0f, 20f)),    // 距出生点 46.5m（见上方 ⚠️：不是"距原点"）
                (4, new Vector3(-40f, 0f, -25f)),   // 距出生点 50.9m
            };
```

> 初稿这里是 `(3, (0,0,-20))` / `(4, (-30,0,-30))` —— **照抄那两行会把缺陷写回去**，已按上面 ⚠️ 的实测更正。当前值由 `MonsterModuleTests` 的几何与行为两条守卫守着。

（循环体不变。）

- [ ] **步骤 5：新增两条技能表项**

`Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` 的 `Table`，在 50002 之后追加：

```csharp
            // 2026-10-12 新怪（spec §3）：t3 骑士精英 / t4 蜘蛛 Boss。
            // maxRange 必须 ≥ monster_config.json 的 attackRange——填小了怪物在攻击距离静默冻结
            //（MonsterModuleTests.Template_AttackRange_Fits_Its_Combat_Rule 守这条）。
            [50003] = new SkillEntry(skillId: 50003, shapeType: 0, maxRange: 2.2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1.5f, baseDamage: 30f, criticalRate: 0f),
            [50004] = new SkillEntry(skillId: 50004, shapeType: 0, maxRange: 2.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 2f, baseDamage: 20f, criticalRate: 0f),
```

- [ ] **步骤 6：新增两条模板**

`Server/Config/monster_config.json` 的 `monsters` 数组末尾追加（注意前一条的 `}` 后加逗号）：

```json
    {
      "templateId": 3,
      "name": "DogKnight",
      "hp": 300,
      "moveSpeed": 2.8,
      "detectionRadius": 18,
      "visionAngle": 120,
      "attackRange": 2.2,
      "patrolRadius": 6,
      "skills": ["Attack"]
    },
    {
      "templateId": 4,
      "name": "SpiderBoss",
      "hp": 1500,
      "moveSpeed": 3.2,
      "detectionRadius": 25,
      "visionAngle": 140,
      "attackRange": 2.5,
      "patrolRadius": 12,
      "skills": ["Attack"]
    }
```

> `skills` 是**装饰性字段**：全仓库没有任何代码读它（服务端走 `SkillIdOf` 硬映射）。照抄只为跟现有条目形制一致。

- [ ] **步骤 7：保底表同步**

`Server/Config/ConfigLoader.cs` 的 `CreateDefaultConfig()` 里，`Wolf` 之后按同一形制加 `DogKnight`(t3) 与 `SpiderBoss`(t4)，数值与步骤 6 **逐字一致**。

> 原因：配置文件缺失时走这张保底表；不补的话 `--monsters 6` 会在 `SpawnMonster(3, …)` 处抛 `Unknown monster template: 3`，把"能跑的降级"变成"启动即崩"。

- [ ] **步骤 8：运行测试验证通过**

运行：`STEST MonsterModuleTests`
预期：PASS，11 passed（原 7 + 新 4）。

- [ ] **步骤 9：Commit**

```bash
git add Server/Config/monster_config.json Server/Config/ConfigLoader.cs \
        Server/Monster/MonsterModule.cs Server/Server.Tests/Monster/MonsterModuleTests.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs
git commit -m "feat(server): elite knight and boss spider join the monster table"
```

---

## 任务 2：快照发送节拍 —— 状态变化即发送

**为什么必须做**（spec §6.1）：`ShouldSend` 的变化判定只看坐标/朝向，静止怪物的状态翻转要等 4 tick 保活，而保活相位按观察者各记一份 —— 同一翻转在不同客户端落在**不同 tick**，跨客户端最多差 ≈267ms。

**文件：**
- 修改：`Server/Aoi/AoiBandScheduler.cs`（`Slot` 结构 + `ShouldSend` 签名与判定）
- 修改：`Server/Aoi/AoiBroadcaster.cs:56-57`（透传两个新参数）
- 测试：`Server/Server.Tests/Aoi/AoiBandSchedulerTests.cs`

- [ ] **步骤 1：写失败的测试**

在 `Server/Server.Tests/Aoi/AoiBandSchedulerTests.cs` 末尾追加：

```csharp
    [Fact]
    public void State_Change_Sends_Immediately_For_A_Stationary_Entity()
    {
        var s = new AoiBandScheduler();
        Assert.True(s.ShouldSend(1, 2, 10f, 100, 0f, 0f, 0f, actionState: 0, dead: false));

        // 静止 + 状态未变：保活窗口（4 tick）未到，不发
        Assert.False(s.ShouldSend(1, 2, 10f, 101, 0f, 0f, 0f, actionState: 0, dead: false));

        // 状态翻转：立即发，不受保活间隔约束
        Assert.True(s.ShouldSend(1, 2, 10f, 102, 0f, 0f, 0f, actionState: 3, dead: false));
    }

    [Fact]
    public void State_Change_Does_Not_Push_Back_The_Position_Cadence()
    {
        var s = new AoiBandScheduler();
        const float mid = 45f;   // 中带 ⇒ IntervalOf = 2 tick

        Assert.True(s.ShouldSend(1, 2, mid, 100, 0f, 0f, 0f, 0, false));    // 首帧
        Assert.True(s.ShouldSend(1, 2, mid, 101, 0f, 0f, 0f, 0, true));     // 状态翻转（dead）——立即发，但**不**重置位置节拍
        Assert.True(s.ShouldSend(1, 2, mid, 102, 0.6f, 0f, 0f, 0, true));   // 距上次位置发送（tick 100）已 2 tick ⇒ 发
    }

    [Fact]
    public void Same_Flip_Is_Sent_To_Two_Observers_On_The_Same_Tick()
    {
        var s = new AoiBandScheduler();
        const long entity = 9;
        Assert.True(s.ShouldSend(1, entity, 10f, 100, 0f, 0f, 0f, 0, false));   // 近带观察者
        Assert.True(s.ShouldSend(2, entity, 70f, 100, 0f, 0f, 0f, 0, false));   // 远带观察者

        // tick 102：两边保活都未到（100 + 4 = 104），若只看坐标/朝向则都不发
        Assert.True(s.ShouldSend(1, entity, 10f, 102, 0f, 0f, 0f, 4, false));
        Assert.True(s.ShouldSend(2, entity, 70f, 102, 0f, 0f, 0f, 4, false));
    }
```

- [ ] **步骤 2：运行测试验证失败**

运行：`STEST AoiBandSchedulerTests`
预期：编译错误 —— `ShouldSend` 不接受 `actionState`/`dead` 参数。

- [ ] **步骤 3：扩展 `Slot` 并改判定**

`Server/Aoi/AoiBandScheduler.cs`，`Slot` 结构加两个字段：

```csharp
        private struct Slot
        {
            public uint LastSentTick;
            public int Band; // 0=近 1=中 2=远
            public float X;
            public float Z;
            public float RotY;
            public byte ActionState;   // 2026-10-12：状态翻转也是变化信号
            public bool Dead;
            public bool HasSent;
        }
```

`ShouldSend` 签名与判定（类注释也要补一句「状态翻转立即发」）：

```csharp
        public bool ShouldSend(long observerId, long entityId, float distance, uint tick,
            float x, float z, float rotY, byte actionState, bool dead)
        {
            int band = BandOf(distance);
            uint interval = IntervalOf(band);

            if (!_state.TryGetValue(observerId, out var entities))
            {
                entities = new Dictionary<long, Slot>();
                _state[observerId] = entities;
            }

            if (!entities.TryGetValue(entityId, out var slot) || !slot.HasSent)
            {
                entities[entityId] = new Slot { LastSentTick = tick, Band = band, X = x, Z = z, RotY = rotY, ActionState = actionState, Dead = dead, HasSent = true };
                return true; // 首帧（或带变化/重见后）立即发
            }

            if (slot.Band != band)
            {
                entities[entityId] = new Slot { LastSentTick = tick, Band = band, X = x, Z = z, RotY = rotY, ActionState = actionState, Dead = dead, HasSent = true };
                return true; // 带变化：重置节拍
            }

            // 状态翻转 → 立即发（跨客户端动画一致性依赖此语义：静止怪物的状态翻转若等保活，
            // 不同观察者的保活相位不同，同一翻转会落在不同 tick，客户端最多差 4 tick ≈267ms）。
            // 注意**不重置 LastSentTick** —— 重置会把位置更新推迟最多一个带间隔（近带 1 / 远带 4 tick）。
            if (slot.ActionState != actionState || slot.Dead != dead)
            {
                slot.ActionState = actionState;
                slot.Dead = dead;
                entities[entityId] = slot;
                return true;
            }

            bool moved = x != slot.X || z != slot.Z || rotY != slot.RotY;
            uint elapsed = tick - slot.LastSentTick;
            bool due = moved ? elapsed >= interval : elapsed >= StaticKeepaliveInterval;

            if (due)
            {
                entities[entityId] = new Slot { LastSentTick = tick, Band = band, X = x, Z = z, RotY = rotY, ActionState = actionState, Dead = dead, HasSent = true };
                return true;
            }
            return false;
        }
```

> `Slot` 是 struct，`slot.ActionState = …` 改的是副本，**必须写回字典**（上面这行 `entities[entityId] = slot;` 不能省）。

- [ ] **步骤 4：调用点透传**

`Server/Aoi/AoiBroadcaster.cs:56`：

```csharp
                if (_scheduler.ShouldSend(observer.PlayerId, entity.PlayerId, distance, tick,
                    entity.Position.X, entity.Position.Z, entity.Rotation,
                    entity.ActionState, !entity.IsAlive))
```

> `Dead` 的取法与同一函数下方构造快照时的 `Dead = !entity.IsAlive` 保持一致。

- [ ] **步骤 5：把服务端墙钟收敛成一个出口（时间基准结构化）**

spec §11 把「快照与广播时间戳同基准」列为**无编译期保护的隐式契约**，并把一条断言测试当作唯一防线。比测试更好的做法是让它变成**结构性事实**：新增唯一出口，三处调用点全部改道。

创建 `Server/ServerClock.cs`：

```csharp
namespace KcpServer
{
    /// <summary>
    /// 服务器墙钟的唯一出口。**快照与施法广播的时间戳必须同基准** —— 客户端拿广播时间戳
    /// 去和快照插值用的渲染时钟比较（spec §6），任一处改用 tick 序号之类的相对量都会
    /// 静默破坏跨客户端一致性。集中到此方法后，「同基准」不再是口头契约。
    /// </summary>
    public static class ServerClock
    {
        public static long NowTicks() => System.DateTime.UtcNow.Ticks;
    }
}
```

替换这三处（值不变，只改道）：

| 文件 | 位置 |
|---|---|
| `Server/Server3C.cs:334` | `ServerTimestamp = System.DateTime.UtcNow.Ticks` |
| `Server/Handler/CastHandler.cs` | 该文件内**全部** `DateTime.UtcNow.Ticks` 调用点（约 96 / 176 / 219 行） |
| `Server/Handler/ClockSyncHandler.cs:25` | `ServerTimestamp = DateTime.UtcNow.Ticks` |

测试 `Server/Server.Tests/Server/ServerClockTests.cs`（新）：

```csharp
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class ServerClockTests
{
    /// <summary>抓的是「基准」而不是具体值：必须落在 DateTime.UtcNow 的秒级邻域内。
    /// 谁把它改成 tick 序号之类的相对量，这条立刻红。</summary>
    [Fact]
    public void Wall_Clock_Is_On_The_UtcNow_Base()
    {
        long before = System.DateTime.UtcNow.Ticks;
        long now = ServerClock.NowTicks();
        long after = System.DateTime.UtcNow.Ticks;

        Assert.InRange(now, before, after);
    }
}
```

- [ ] **步骤 6：运行测试验证通过**

运行：`STEST AoiBandSchedulerTests`
预期：PASS（原 8 + 新 3）。

再跑带宽口径回归（本任务最可能碰坏的地方）：
运行：`STEST Aoi` — 预期全绿，含 `BandwidthBudgetTests`（预算常量不需要动）
运行：`STEST ServerClockTests` — 预期 PASS
运行：`STEST Handler` — 预期全绿（CastHandler / ClockSyncHandler 改道后回归）

- [ ] **步骤 7：Commit**

```bash
git add Server/Aoi/AoiBandScheduler.cs Server/Aoi/AoiBroadcaster.cs \
        Server/Server.Tests/Aoi/AoiBandSchedulerTests.cs \
        Server/ServerClock.cs Server/Server3C.cs \
        Server/Handler/CastHandler.cs Server/Handler/ClockSyncHandler.cs \
        Server/Server.Tests/Server/ServerClockTests.cs
git commit -m "fix(server): send a snapshot the moment an entity changes action state"
```

---

## 任务 3：客户端状态时间轴

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteInterpolator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`（`RemoteEntityView` 字段、`UpdateRender` 签名、`CreateView` 调用点、`ApplySnapshot` 采样入参）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteInterpolatorTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs`

- [ ] **步骤 1：写失败的测试**

`RemoteInterpolatorTests.cs` 追加：

```csharp
    [Test]
    public void Render_State_Is_A_Step_Function_Of_The_Earlier_Sample()
    {
        var interp = new RemoteInterpolator();
        interp.AddSample(1, 1_000L, new Vec3(0f, 0f, 0f), 0f, actionState: 0, dead: false);
        interp.AddSample(1, 1_100L, new Vec3(10f, 0f, 0f), 0f, actionState: 2, dead: false);

        // 渲染时刻落在两样本之间 → 状态取**较早**那个样本（阶梯，不插值）
        // 算式：nowServerTicks 1_150 − renderDelayTicks 100 = renderAt **1_050**，落在样本 1_000/1_100 之间，
        // 插值系数 0.5 ⇒ 位置 5f；状态取较早样本 ⇒ 0。
        // （初稿这里写的是 1_105，renderAt=1_005 ⇒ 位置 0.5，与断言 5f 对不上——是笔误。）
        Assert.IsTrue(interp.TryGetRenderState(1, 1_150L, 100L,
            out var pos, out _, out var actionState, out var dead));
        Assert.AreEqual(0, actionState, "状态是阶梯函数——取时间戳 ≤ renderAt 的最后一个样本");
        Assert.IsFalse(dead);
        Assert.AreEqual(5f, pos.x, 0.001f, "位置仍然插值");
    }

    [Test]
    public void Dead_Flag_Is_Sampled_Too()
    {
        var interp = new RemoteInterpolator();
        interp.AddSample(7, 500L, new Vec3(1f, 0f, 1f), 0f, actionState: 6, dead: false);
        interp.AddSample(7, 600L, new Vec3(1f, 0f, 1f), 0f, actionState: 6, dead: true);

        Assert.IsTrue(interp.TryGetRenderState(7, 650L, 100L, out _, out _, out var a, out var dead));
        Assert.AreEqual(6, a);
        Assert.IsFalse(dead, "renderAt=550 时第二样本还没到");

        Assert.IsTrue(interp.TryGetRenderState(7, 750L, 100L, out _, out _, out _, out dead));
        Assert.IsTrue(dead, "renderAt=650 时已越过第二样本");
    }
```

> 注意：`TryGetRenderState` 的既有调用方与既有用例需要同步补两个 out 参数 —— 先改签名再加新用例，编译错误会把所有调用点指出来。

`RemoteEntityRegistryTests.cs` 追加：

```csharp
    [Test]
    public void Npc_View_Keeps_Sampled_State_Instead_Of_Arrival_State()
    {
        // 采样路径：视图的 ActionState 由 UpdateRender 在 renderAt 处采出
        //（ApplySnapshot 仍写 view.ActionState 作为"最后已知值"，供测试与观察用）
        _registry.ApplySnapshot(new EntitySnapshot
        {
            PlayerId = 2_000_001,
            FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                | EntityFieldMask.ActionState | EntityFieldMask.TemplateId),
            X = 0f, Z = 0f, Kind = (byte)EntityKind.Npc, TemplateId = 2,
            ActionState = (byte)NpcAnimationState.Run, Tick = 1
        }, serverTicks: 1_000_000L);

        Assert.IsTrue(_registry.TrySampleNpcState(2_000_001, renderAt: 1_000_000L,
            out var state, out var dead));
        Assert.AreEqual((byte)NpcAnimationState.Run, state);
        Assert.IsFalse(dead);
    }
```

- [ ] **步骤 2：运行测试验证失败**

运行：`UTEST RemoteInterpolatorTests`
预期：编译错误 —— `AddSample` 不接受 `actionState`/`dead`；`TryGetRenderState` 不接受两个新 out。

- [ ] **步骤 3：扩展采样缓冲**

`RemoteInterpolator.cs`：

```csharp
        /// <summary>入样本。actionState/dead 与位置样本同源同 tick —— 状态按阶梯采样，位置按插值。</summary>
        public void AddSample(long playerId, long serverTicks, Vec3 position, float rotationY,
            byte actionState, bool dead)
        {
            if (!_buffers.TryGetValue(playerId, out var samples))
            {
                samples = new List<Sample>();
                _buffers[playerId] = samples;
            }
            samples.Add(new Sample
            {
                ServerTicks = serverTicks, Position = position, RotationY = rotationY,
                ActionState = actionState, Dead = dead
            });
            if (samples.Count > MaxSamplesPerEntity)
                samples.RemoveAt(0);
        }
```

`TryGetRenderState` 增加两个 out，并在**三条返回路径**上填值（阶梯语义）：

```csharp
        public bool TryGetRenderState(long playerId, long nowServerTicks, long renderDelayTicks,
            out Vector3 position, out Quaternion rotation, out byte actionState, out bool dead)
        {
            position = default;
            rotation = default;
            actionState = 0;
            dead = false;
            if (!_buffers.TryGetValue(playerId, out var samples) || samples.Count == 0)
                return false;

            long renderAt = nowServerTicks - renderDelayTicks;

            // 最新样本仍早于渲染时刻 → 用最新（快照不足场景）
            if (renderAt >= samples[samples.Count - 1].ServerTicks)
            {
                var last = samples[samples.Count - 1];
                position = ToUnity(last.Position);
                rotation = Quaternion.Euler(0f, last.RotationY, 0f);
                actionState = last.ActionState;
                dead = last.Dead;
                return true;
            }

            for (int i = 0; i < samples.Count - 1; i++)
            {
                var a = samples[i];
                var b = samples[i + 1];
                if (renderAt >= a.ServerTicks && renderAt <= b.ServerTicks)
                {
                    long span = b.ServerTicks - a.ServerTicks;
                    float t = span <= 0 ? 0f : (float)(renderAt - a.ServerTicks) / span;
                    position = Vector3.LerpUnclamped(ToUnity(a.Position), ToUnity(b.Position), t);
                    rotation = Quaternion.Slerp(Quaternion.Euler(0f, a.RotationY, 0f), Quaternion.Euler(0f, b.RotationY, 0f), t);
                    // 状态是阶梯：取较早样本（不随 t 混合）
                    actionState = a.ActionState;
                    dead = a.Dead;
                    return true;
                }
            }

            var first = samples[0];
            position = ToUnity(first.Position);
            rotation = Quaternion.Euler(0f, first.RotationY, 0f);
            actionState = first.ActionState;
            dead = first.Dead;
            return true;
        }
```

`Sample` 结构补两个字段：

```csharp
        private struct Sample
        {
            public long ServerTicks;
            public Vec3 Position;
            public float RotationY;
            public byte ActionState;
            public bool Dead;
        }
```

- [ ] **步骤 4：视图挂驱动字段 + 采样入口**

`RemoteEntityRegistry.cs`：

`RemoteEntityView` 增加并行字段（与既有 `Animator` 同惯例，非真人走 `NpcAnimator`）：

```csharp
        /// <summary>怪物动画驱动（Kind=Npc 且预制体带组件时非空；球体占位为 null）</summary>
        public NpcEntityAnimator? NpcAnimator { get; }
```

构造函数加参数（默认 null，保持既有调用点可用）：

```csharp
        public RemoteEntityView(GameObject go, byte kind = 0, int templateId = 0,
            RemoteEntityAnimator? animator = null, NpcEntityAnimator? npcAnimator = null)
        {
            GameObject = go;
            Kind = kind;
            TemplateId = templateId;
            Animator = animator;
            NpcAnimator = npcAnimator;
        }
```

新增采样入口（`UpdateRender` 与测试共用）：

```csharp
        /// <summary>采出某视图在 renderAt 处的状态（阶梯）。供 UpdateRender 与测试使用。</summary>
        public bool TrySampleNpcState(long entityId, long renderAt, out byte actionState, out bool dead)
        {
            actionState = 0;
            dead = false;
            return _interpolator.TryGetSampleState(entityId, renderAt, out actionState, out dead);
        }
```

`RemoteInterpolator` 补一个只读状态的轻量查询（避免为了取状态去造 position/rotation 的 out）：

```csharp
        /// <summary>只取状态（阶梯语义，与 TryGetRenderState 同一规则）——供渲染循环按 renderAt 采样。</summary>
        public bool TryGetSampleState(long playerId, long renderAt, out byte actionState, out bool dead)
        {
            actionState = 0;
            dead = false;
            if (!_buffers.TryGetValue(playerId, out var samples) || samples.Count == 0)
                return false;

            // 时间戳 ≤ renderAt 的最后一个样本；全部晚于 renderAt 时用最旧（与位置路径的 fallback 一致）
            Sample chosen = samples[0];
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].ServerTicks <= renderAt) chosen = samples[i];
                else break;
            }
            actionState = chosen.ActionState;
            dead = chosen.Dead;
            return true;
        }
```

`ApplySnapshot` 里把初始样本带上状态（`AddSample(...)` 的**唯一**调用点在 `RemoteEntityRegistry.cs:71`，补上 `snapshot.ActionState` 与 `snapshot.Dead` 两个实参），`UpdateRender` 暂时不接驱动（任务 4 落地后再接）。

- [ ] **步骤 5：运行测试验证通过**

运行：`UTEST RemoteInterpolatorTests`
运行：`UTEST RemoteEntityRegistryTests`
预期：两者全绿（含既有用例 —— 位置插值行为不得改变）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteInterpolator.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/RemoteInterpolatorTests.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs
git commit -m "feat(sync): the snapshot buffer carries action state, sampled at render time"
```

---

## 任务 4：怪物动画驱动

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NpcAttackScheduleTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NpcEntityAnimatorTests.cs`

- [ ] **步骤 1：写失败的测试（纯逻辑部分）**

创建 `NpcAttackScheduleTests.cs`：

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>攻击排程：变体必须由广播时间戳推出（不能靠客户端自增计数——AOI 边缘进出的客户端会错位）。</summary>
    public class NpcAttackScheduleTests
    {
        [Test]
        public void Variant_Is_A_Pure_Function_Of_The_Timestamp()
        {
            // 钉住实际取值（只断言 f(t) == f(t) 是恒真式，挡不住"改成按客户端计数"这种回归）
            Assert.AreEqual(0, NpcAttackSchedule.VariantOf(0L));
            Assert.AreEqual(1, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks));
            Assert.AreEqual(0, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks * 2));

            // 纯函数：同一时间戳任意次调用结果一致（所有客户端各算各的也必须同结论）
            long t = 638_000_000_000_000_000L;
            Assert.AreEqual(NpcAttackSchedule.VariantOf(t), NpcAttackSchedule.VariantOf(t));
        }

        [Test]
        public void A_Swing_Sequence_Shows_Both_Variants()
        {
            // 变体是时间戳的纯函数（跨客户端一致，这是硬要求）；"交替"只是 cadence 的副产物——
            // 1.5s 冷却 = 22.5 个桶，桶序号每次 +22 或 +23，奇偶每两刀翻一次。
            // 这里断言**真正成立**的性质：一段连续挥砍里两段攻击都出现过（否则玩家永远只看同一段）。
            long t0 = 638_000_000_000_000_000L;
            var seen = new HashSet<int>();
            for (int k = 0; k < 10; k++)
                seen.Add(NpcAttackSchedule.VariantOf(t0 + k * 1_500L * 10_000L));

            Assert.AreEqual(2, seen.Count, "连续 10 刀里两段攻击都应出现过");
        }

        [Test]
        public void Drain_Only_Fires_Items_Whose_Time_Has_Come()
        {
            var schedule = new NpcAttackSchedule();
            schedule.Schedule(entityId: 7, serverTicks: 1_000L);
            var due = new List<NpcAttackSchedule.DueAttack>();

            schedule.Drain(renderAt: 999L, due);
            Assert.AreEqual(0, due.Count, "未到点不得触发");

            schedule.Drain(renderAt: 1_000L, due);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(7, due[0].EntityId);

            due.Clear();
            schedule.Drain(renderAt: 1_001L, due);
            Assert.AreEqual(0, due.Count, "**不得重播**——drain 要出队");
        }

        [Test]
        public void Late_Arrival_Fires_Immediately_Instead_Of_Being_Dropped()
        {
            var schedule = new NpcAttackSchedule();
            schedule.Schedule(7, serverTicks: 1_000L);
            var due = new List<NpcAttackSchedule.DueAttack>();

            schedule.Drain(renderAt: 5_000L, due);   // 卡顿/刚进视野：时间戳早已过去

            Assert.AreEqual(1, due.Count, "迟到要立即播，不能丢");
        }

        [Test]
        public void Clear_For_Entity_Drops_Its_Pending_Attacks()
        {
            var schedule = new NpcAttackSchedule();
            schedule.Schedule(7, 1_000L);
            schedule.Schedule(8, 1_000L);
            schedule.Clear(7);

            var due = new List<NpcAttackSchedule.DueAttack>();
            schedule.Drain(2_000L, due);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(8, due[0].EntityId);
        }
    }
}
```

创建 `NpcEntityAnimatorTests.cs`（映射表逐值 + **暂停/确认规则**，用真实 Animator 驱动）：

```csharp
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>驱动侧的参数写入规则（spec §5.2 四条规则）。</summary>
    public class NpcEntityAnimatorTests
    {
        [Test]
        public void Map_State_Covers_The_Protocol_Enum()
        {
            Assert.AreEqual(NpcEntityAnimator.StateIdle, NpcEntityAnimator.MapState((byte)NpcAnimationState.Idle, false));
            Assert.AreEqual(NpcEntityAnimator.StateWalk, NpcEntityAnimator.MapState((byte)NpcAnimationState.Walk, false));
            Assert.AreEqual(NpcEntityAnimator.StateRun, NpcEntityAnimator.MapState((byte)NpcAnimationState.Run, false));
            Assert.AreEqual(NpcEntityAnimator.StateAttacking, NpcEntityAnimator.MapState((byte)NpcAnimationState.Attack, false));
            Assert.AreEqual(NpcEntityAnimator.StateHit, NpcEntityAnimator.MapState((byte)NpcAnimationState.Hit, false));
            Assert.AreEqual(NpcEntityAnimator.StateDeath, NpcEntityAnimator.MapState((byte)NpcAnimationState.Death, false));

            // 忽略值：保持当前（返回 -1 表示"不写"）
            Assert.AreEqual(-1, NpcEntityAnimator.MapState((byte)NpcAnimationState.Skill, false));
            Assert.AreEqual(-1, NpcEntityAnimator.MapState((byte)NpcAnimationState.Down, false));
            Assert.AreEqual(-1, NpcEntityAnimator.MapState((byte)NpcAnimationState.GetUp, false));

            // Dead 位优先于一切
            Assert.AreEqual(NpcEntityAnimator.StateDeath, NpcEntityAnimator.MapState((byte)NpcAnimationState.Run, dead: true));
        }
```

> **暂停/确认规则**的用例写在同文件、用**真实 Animator + 临时控制器**驱动。临时控制器在 `[SetUp]` 里构建、`[TearDown]` 里删除（`AssetDatabase.DeleteAsset`）——顺手写出来：

```csharp
    private GameObject _go = null!;
    private Animator _animator = null!;
    private NpcEntityAnimator _driver = null!;
    private const string TempControllerPath = "Assets/Tests_TempNpc.controller";

    [SetUp]
    public void SetUp()
    {
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(TempControllerPath);
        // 枚举在 UnityEngine 下，不在 UnityEditor.Animations 里
        controller.AddParameter("MonsterState", UnityEngine.AnimatorControllerParameterType.Int);
        controller.AddParameter("AttackVariant", UnityEngine.AnimatorControllerParameterType.Int);
        controller.AddParameter("Attack", UnityEngine.AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        var run = sm.AddState("Run");
        var attack = sm.AddState("Attack01");
        sm.defaultState = idle;

        var toRun = sm.AddAnyStateTransition(run);
        toRun.canTransitionToSelf = false;
        toRun.duration = 0.1f;          // 显式给混合时长：默认 0.25s 会让断言落在过渡中途
        toRun.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, 2f, "MonsterState");

        var toAttack = sm.AddAnyStateTransition(attack);
        toAttack.canTransitionToSelf = false;
        toAttack.duration = 0.1f;
        toAttack.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Attack");

        var backToIdle = attack.AddTransition(idle);      // exit-time 回 Idle
        backToIdle.hasExitTime = true;
        backToIdle.exitTime = 1f;
        backToIdle.duration = 0f;

        _go = new GameObject("NpcDriverTest");
        _animator = _go.AddComponent<Animator>();
        _animator.runtimeAnimatorController = controller;
        _driver = _go.AddComponent<NpcEntityAnimator>();
        _driver.ResolveAnimator();   // EditMode 下 AddComponent 不触发 Awake —— 必须显式解析一次
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_go);
        UnityEditor.AssetDatabase.DeleteAsset(TempControllerPath);
    }

    private string CurrentState()
    {
        // 必须推进足够跨越混合时长（fixture 里设的是 0.1s）再断言：
        // 过渡期间 GetCurrentAnimatorStateInfo 报的是**源态**，只推 50ms 会读到上一个状态，
        // 断言就会在"过渡还没完成"这件事上假红/假绿。
        _animator.Update(0.3f);
        var info = _animator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName("Run")) return "Run";
        if (info.IsName("Attack01")) return "Attack01";
        if (info.IsName("Idle")) return "Idle";
        return "<unknown>";
    }

    [Test]
    public void Timeline_State_Reaches_The_Animator()
    {
        _driver.ApplyState((byte)NpcAnimationState.Run, false);
        Assert.AreEqual("Run", CurrentState());
    }

    [Test]
    public void A_Stale_Locomotion_Sample_Does_Not_Interrupt_The_Swing()
    {
        _driver.ApplyState((byte)NpcAnimationState.Run, false);
        _driver.PlayAttack(variant: 0);
        Assert.AreEqual("Attack01", CurrentState());

        // 竞态：施法广播与状态快照同 tick 发出，到达顺序不保证——
        // 广播先到时会先起手，紧接着来的还是**上一 tick 的** Run 样本
        _driver.ApplyState((byte)NpcAnimationState.Run, false);
        Assert.AreEqual("Attack01", CurrentState(), "确认前不得被陈旧的 locomotion 样本打断");
    }

    [Test]
    public void Leaving_The_Attack_Window_Releases_The_Busy_Flag()
    {
        _driver.ApplyState((byte)NpcAnimationState.Run, false);
        _driver.PlayAttack(0);
        _driver.ApplyState((byte)NpcAnimationState.Attack, false);   // 确认
        _driver.ApplyState((byte)NpcAnimationState.Run, false);      // 服务端离开攻击窗口
        Assert.AreEqual("Run", CurrentState(), "离开攻击窗口后必须把时间轴状态写回去");
    }
```


- [ ] **步骤 2：运行测试验证失败**

运行：`UTEST NpcAttackScheduleTests`
运行：`UTEST NpcEntityAnimatorTests`
预期：编译错误 —— 两个类都不存在。

- [ ] **步骤 3：实现排程队列**

创建 `NpcAttackSchedule.cs`：

```csharp
using System.Collections.Generic;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 怪物攻击的**呈现排程**（spec §6.2）。每个客户端都收到**同一个** CastBroadcast 对象
    /// （服务端构造一次、发给全部观察者），所以「第几刀用哪段攻击」必须由时间戳推出，
    /// 而不是客户端自增计数 —— 在 AOI 边缘进出过的客户端计数器会错位，同一刀播不同动画。
    ///
    /// 排程项在渲染时钟越过它时触发，与位置插值共用同一根服务器时间轴。
    /// </summary>
    public sealed class NpcAttackSchedule
    {
        /// <summary>变体分桶：≈1/15 秒（.NET ticks 是 100ns）。**客户端共享常量**，
        /// 不随登录返回的 ServerTickRateMs 变 —— 一致性只要求所有客户端用同一个值。</summary>
        public const long VariantBucketTicks = 666_667L;

        public readonly struct DueAttack
        {
            public readonly long EntityId;
            public readonly int Variant;
            public readonly long ServerTicks;
            public DueAttack(long entityId, int variant, long serverTicks)
            {
                EntityId = entityId;
                Variant = variant;
                ServerTicks = serverTicks;
            }
        }

        private readonly List<(long EntityId, long ServerTicks)> _pending = new List<(long, long)>();

        /// <summary>服务器裁定的第几段攻击（0 → Attack01，1 → Attack02）</summary>
        public static int VariantOf(long serverTicks) => (int)((serverTicks / VariantBucketTicks) % 2);

        public void Schedule(long entityId, long serverTicks) => _pending.Add((entityId, serverTicks));

        /// <summary>取出所有到点的项（timestamp &lt;= renderAt）并出队。迟到的立即触发，不丢弃。</summary>
        public void Drain(long renderAt, List<DueAttack> into)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].ServerTicks > renderAt) continue;
                into.Add(new DueAttack(_pending[i].EntityId, VariantOf(_pending[i].ServerTicks), _pending[i].ServerTicks));
                _pending.RemoveAt(i);
            }
            // 同帧多项时按时间戳升序，保证变体顺序稳定
            into.Sort((a, b) => a.ServerTicks.CompareTo(b.ServerTicks));
        }

        /// <summary>视图销毁时清掉它的待播项（避免播给已不存在的实体）</summary>
        public void Clear(long entityId) => _pending.RemoveAll(p => p.EntityId == entityId);

        public void ClearAll() => _pending.Clear();
    }
}
```


- [ ] **步骤 4：实现驱动组件**

创建 `NpcEntityAnimator.cs`：

```csharp
using Cool.Protocol;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 怪物动画驱动（spec §5）。两个驱动源分工明确：
    ///   · **快照**（在 renderAt 处采样）→ Idle / Walk / Run / Hit / Death
    ///   · **施法广播**（排程到点）→ 每次挥砍播一次攻击，变体由时间戳推
    ///
    /// 为什么需要忙位（规则见 spec §5.2）：AnyState 转移每帧评估，交战中服务端**全程**
    /// 保持 ActionState=Attack，若不屏蔽 locomotion 写入，已经播上的挥砍会被当场打断。
    ///
    /// 前提：怪物预制体是纯美术（无脚本、无碰撞体）。本组件**不做** RemoteEntityAnimator
    /// 那套「禁用实例上其它 MonoBehaviour」——谁往怪物预制体里加脚本，谁自己处理。
    /// </summary>
    public sealed class NpcEntityAnimator : MonoBehaviour
    {
        public const int StateIdle = 0;
        public const int StateWalk = 1;
        public const int StateRun = 2;
        public const int StateAttacking = 3;
        public const int StateHit = 4;
        public const int StateDeath = 5;

        private static readonly int MonsterStateParam = Animator.StringToHash("MonsterState");
        private static readonly int AttackVariantParam = Animator.StringToHash("AttackVariant");
        private static readonly int AttackTrigger = Animator.StringToHash("Attack");

        private Animator? _animator;
        private int _lastWritten = int.MinValue;   // 去重：状态没变不重写参数

        private bool _busy;              // 攻击片段在播 / 等待时间轴确认
        private bool _awaitingConfirm;   // 已触发挥砍，等时间轴证实服务端进了攻击窗口

        private void Awake() => ResolveAnimator();

        /// <summary>
        /// 解析 Animator。**internal 是为了测试**：EditMode 下 `AddComponent` **不会触发 `Awake`**
        /// （只在 play mode 生效），测试必须自己能调一次；用反射硬调 Awake 会踩 Unity 的
        /// `ShouldRunBehaviour` 断言，所以留这个命名的方法。
        ///
        /// 用 GetComponentInChildren：本组件由 registry 在实例化后 AddComponent 到**预制体根**，
        /// 而模型自带的 Animator 可能在子节点上（美术资源的常见结构）。在 MonoBehaviour 内部
        /// 取组件是合规的——registry 是普通类，项目规则禁止它在非 MonoBehaviour 上下文里 GetComponent。
        /// </summary>
        internal void ResolveAnimator()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null && _animator.runtimeAnimatorController == null)
                _animator = null;   // 没有控制器的 Animator 写参数只会刷错误日志
        }

        /// <summary>快照状态（由调用方在 renderAt 处采样后传入）。规则 2/3/4。</summary>
        public void ApplyState(byte actionState, bool dead)
        {
            int state = MapState(actionState, dead);
            if (state < 0) return;                       // Skill/Down/GetUp：忽略

            if (_awaitingConfirm)
            {
                if (state == StateAttacking || state == StateHit || state == StateDeath)
                    _awaitingConfirm = false;            // 服务端确认进了攻击窗口（或被打断）
                else
                    return;                              // 确认前一律不写：广播可能先于快照到达
            }

            if (_busy)
            {
                if (state == StateAttacking) return;     // 攻击窗口内保持 3，别让 locomotion 转移打断挥砍
                _busy = false;                           // 服务端离开攻击窗口 → **释放**（漏了这句就是永久锁存：
                                                         // 怪物挥完第一刀后再也不写时间轴，既不播受击也不播死亡）
            }

            Write(state);
        }

        /// <summary>服务端裁定的一次挥砍（排程到点后调用）。规则 1。</summary>
        public void PlayAttack(int variant)
        {
            _busy = true;
            _awaitingConfirm = true;
            Write(StateAttacking);                       // 先写值再打 Trigger：广播与快照到达顺序不保证
            if (_animator == null) return;
            _animator.SetInteger(AttackVariantParam, variant);
            _animator.SetTrigger(AttackTrigger);
        }

        private void Write(int state)
        {
            if (state == _lastWritten) return;
            _lastWritten = state;
            if (_animator == null) return;
            _animator.SetInteger(MonsterStateParam, state);
        }

        /// <summary>线上 ActionState → 控制器参数值。返回 -1 表示"不写、保持当前"。</summary>
        public static int MapState(byte actionState, bool dead)
        {
            if (dead) return StateDeath;                 // Dead 位优先（与真人侧同惯例）
            switch ((NpcAnimationState)actionState)
            {
                case NpcAnimationState.Idle:   return StateIdle;
                case NpcAnimationState.Walk:   return StateWalk;
                case NpcAnimationState.Run:    return StateRun;
                case NpcAnimationState.Attack: return StateAttacking;
                case NpcAnimationState.Hit:    return StateHit;
                case NpcAnimationState.Death:  return StateDeath;
                default:                       return -1;   // Skill / Down / GetUp
            }
        }
    }
}
```

> **已知前提（写进注释，别让它悄悄失效）**：`_awaitingConfirm` 依赖「服务端在排队施法的同一次 BT 动作里就置了 `ActionState = Attack`」（`MonsterBtActions.Attack` 确实如此）。若哪天改成"先施法、稍后置状态"，忙位不会解除 —— `ApplyState` 里的 Hit/Death 分支是唯一兜底。

- [ ] **步骤 5：运行测试验证通过**

运行：`UTEST NpcAttackScheduleTests`
运行：`UTEST NpcEntityAnimatorTests`
预期：全绿。`NpcEntityAnimatorTests` 的 Animator 用例若因临时控制器构建失败而红，先修 fixture（`[TearDown]` 必须删掉 `Assets/Tests_TempNpc.controller`，否则污染资产库）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcAttackSchedule.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/NpcAttackScheduleTests.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/NpcEntityAnimatorTests.cs
git commit -m "feat(sync): a monster animation driver fed by snapshots and cast broadcasts"
```

---

## 任务 5：两个动画控制器资产

**文件：**
- 创建：`Assets/Monstor/DogKnight/Animator/NpcAnimator_DogKnight.controller`
- 创建：`Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NpcAnimatorAssetTests.cs`

- [ ] **步骤 1：写失败的测试（资产断言）**

创建 `NpcAnimatorAssetTests.cs`：

```csharp
using System.Linq;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>控制器资产断言：结构被改坏时立刻红（驱动侧只认参数名，名字错=动画静默不动）。</summary>
    public class NpcAnimatorAssetTests
    {
        private static readonly string[] Controllers =
        {
            "Assets/Monstor/DogKnight/Animator/NpcAnimator_DogKnight.controller",
            "Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller",
        };

        private static readonly string[] ExpectedStates =
        {
            "Idle", "Walk", "Run", "Attack01", "Attack02", "Hit", "Death"
        };

        [Test]
        public void Both_Controllers_Exist_With_The_Expected_States()
        {
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                Assert.IsNotNull(controller, path);

                var names = controller!.layers[0].stateMachine.states.Select(s => s.state.name).ToArray();
                CollectionAssert.AreEquivalent(ExpectedStates, names, path);
            }
        }

        [Test]
        public void Parameters_Match_What_The_Driver_Hashes()
        {
            // 驱动的参数名（Animator.StringToHash 常量）与资产必须成对；对不上 = 参数永远写不进去
            var expected = new[] { "MonsterState", "AttackVariant", "Attack" };
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var actual = controller.parameters.Select(p => p.name).ToArray();
                CollectionAssert.AreEquivalent(expected, actual, path);
            }
        }

        [Test]
        public void Hit_And_Death_Have_No_Outgoing_Transitions()
        {
            // 受击/死亡不放出边：受击片段若短于服务端硬直窗口，AnyState 会顺着仍成立的
            // MonsterState==4 逐帧重播受击；死亡本就该定格。
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var states = controller.layers[0].stateMachine.states;
                foreach (var name in new[] { "Hit", "Death" })
                {
                    var st = states.First(s => s.state.name == name).state;
                    Assert.AreEqual(0, st.transitions.Length, $"{path}:{name}");
                }
            }
        }

        [Test]
        public void Attack_States_Return_Via_Exit_Time()
        {
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!;
                var states = controller.layers[0].stateMachine.states;
                foreach (var name in new[] { "Attack01", "Attack02" })
                {
                    var st = states.First(s => s.state.name == name).state;
                    Assert.AreEqual(1, st.transitions.Length, $"{path}:{name} 应有一条 exit-time 回 Idle 的出边");
                    Assert.IsTrue(st.transitions[0].hasExitTime, $"{path}:{name}");
                    Assert.AreEqual("Idle", st.transitions[0].destinationState.name, $"{path}:{name}");
                }
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`UTEST NpcAnimatorAssetTests`
预期：FAIL —— 控制器资产不存在（`Assert.IsNotNull` 失败）。

- [ ] **步骤 3：创建两个控制器骨架**

两个控制器**结构完全相同**，只有片段与状态名映射不同：

| 状态 | DogKnight 片段 | Spider 片段 |
|---|---|---|
| Idle | `Assets/Monstor/DogKnight/Animations/Idle_Battle.anim` | `spider_myOldOne_Mecanim.FBX` 内嵌 `idle` |
| Walk | `…/WalkForwardBattle.anim` | 内嵌 `walk` |
| Run | `…/RunForwardBattle.anim` | 内嵌 `run` |
| Attack01 | `…/Attack01.anim` | 内嵌 `attack1` |
| Attack02 | `…/Attack02.anim` | 内嵌 `attack2` |
| Hit | `…/GetHit.anim` | 内嵌 `hit1` |
| Death | `…/Die.anim` | 内嵌 `death1` |

先建骨架：

```
MCP animator-create '{"sourcePaths":["Assets/Monstor/DogKnight/Animator/NpcAnimator_DogKnight.controller","Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller"]}'
```

再用 `animator-modify` 逐项落地（每只怪一次调用，`modifications` 数组按序）：

```json
[
  {"type":"AddParameter","parameterName":"MonsterState","parameterType":"Int","defaultInt":0},
  {"type":"AddParameter","parameterName":"AttackVariant","parameterType":"Int","defaultInt":0},
  {"type":"AddParameter","parameterName":"Attack","parameterType":"Trigger"},
  {"type":"AddState","layerName":"Base Layer","stateName":"Idle","motionAssetPath":"<见上表>"},
  {"type":"AddState","layerName":"Base Layer","stateName":"Walk","motionAssetPath":"<见上表>"},
  {"type":"AddState","layerName":"Base Layer","stateName":"Run","motionAssetPath":"<见上表>"},
  {"type":"AddState","layerName":"Base Layer","stateName":"Attack01","motionAssetPath":"<见上表>"},
  {"type":"AddState","layerName":"Base Layer","stateName":"Attack02","motionAssetPath":"<见上表>"},
  {"type":"AddState","layerName":"Base Layer","stateName":"Hit","motionAssetPath":"<见上表>"},
  {"type":"AddState","layerName":"Base Layer","stateName":"Death","motionAssetPath":"<见上表>"},
  {"type":"SetDefaultState","layerName":"Base Layer","stateName":"Idle"},
  {"type":"AddAnyStateTransition","layerName":"Base Layer","destinationStateName":"Idle","duration":0.15,"hasExitTime":false,"conditions":[{"parameter":"MonsterState","mode":"Equals","threshold":0}]},
  {"type":"AddAnyStateTransition","layerName":"Base Layer","destinationStateName":"Walk","duration":0.15,"hasExitTime":false,"conditions":[{"parameter":"MonsterState","mode":"Equals","threshold":1}]},
  {"type":"AddAnyStateTransition","layerName":"Base Layer","destinationStateName":"Run","duration":0.15,"hasExitTime":false,"conditions":[{"parameter":"MonsterState","mode":"Equals","threshold":2}]},
  {"type":"AddAnyStateTransition","layerName":"Base Layer","destinationStateName":"Hit","duration":0.1,"hasExitTime":false,"conditions":[{"parameter":"MonsterState","mode":"Equals","threshold":4}]},
  {"type":"AddAnyStateTransition","layerName":"Base Layer","destinationStateName":"Death","duration":0.1,"hasExitTime":false,"conditions":[{"parameter":"MonsterState","mode":"Equals","threshold":5}]},
  {"type":"AddAnyStateTransition","layerName":"Base Layer","destinationStateName":"Attack01","duration":0.1,"hasExitTime":false,"conditions":[{"parameter":"AttackVariant","mode":"Equals","threshold":0},{"parameter":"Attack","mode":"If"}]},
  {"type":"AddAnyStateTransition","layerName":"Base Layer","destinationStateName":"Attack02","duration":0.1,"hasExitTime":false,"conditions":[{"parameter":"AttackVariant","mode":"Equals","threshold":1},{"parameter":"Attack","mode":"If"}]},
  {"type":"AddTransition","layerName":"Base Layer","sourceStateName":"Attack01","destinationStateName":"Idle","hasExitTime":true,"exitTime":0.95,"hasFixedDuration":true,"duration":0.1,"conditions":[]},
  {"type":"AddTransition","layerName":"Base Layer","sourceStateName":"Attack02","destinationStateName":"Idle","hasExitTime":true,"exitTime":0.95,"hasFixedDuration":true,"duration":0.1,"conditions":[]}
]
```

- [ ] **步骤 4：关掉 AnyState 重入 + 关根运动**

`animator-modify` 不支持 `canTransitionToSelf`，用 `script-execute` 收尾（**改完必须验**——本项目在 script-execute 写资产上有过反序列化不正确的前科）：

```csharp
foreach (var path in new[] {
    "Assets/Monstor/DogKnight/Animator/NpcAnimator_DogKnight.controller",
    "Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller" })
{
    var c = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
    int fixedCount = 0;
    foreach (var layer in c.layers)
    {
        foreach (var t in layer.stateMachine.anyStateTransitions)
        {
            t.canTransitionToSelf = false;   // 条件持续为真时不重入（否则受击/移动动画逐帧重播）
            fixedCount++;
        }
    }
    UnityEditor.EditorUtility.SetDirty(c);
    Debug.Log("[NPC-CTRL] " + path + " anyState=" + fixedCount);
}
UnityEditor.AssetDatabase.SaveAssets();
```

然后 `MCP assets-refresh '{}'`，再跑步骤 1 的断言测试**回读验证**。

> ⚠️ **光跑测试不足以验收（2026-10-12 实锤）**：任务 5 首轮就把 `Attack` 参数丢在了磁盘上 —— 磁盘与 HEAD 都只有 2 个参数，可 Unity 内存里是 3 个，于是 8 个断言全绿、提交的字节却是坏的。**必须同时给出字节级证据**：`grep -c 'm_Name: Attack$' <controller>` 等于 1（每个参数各一次），外加一段把两个控制器的参数名+类型打印出来的 `LoadAssetAtPath` 输出，证明内存视图与磁盘一致。变异验证之后同理 —— 回读**磁盘字节**确认变异已还原。

- [ ] **步骤 5：运行测试验证通过**

运行：`UTEST NpcAnimatorAssetTests`
预期：4 个用例全绿。

再补一条**控制器与驱动的端到端**用例（同文件追加）—— 这是"参数名错 = 动画静默不动"的唯一防线（`SetInteger` 写进不存在的参数**不报错**）：

```csharp
        [Test]
        public void Driver_And_Real_Controller_Agree_On_Parameter_Names()
        {
            var go = new GameObject("DriverOnRealController");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/Monstor/DogKnight/Animator/NpcAnimator_DogKnight.controller");
                var driver = go.AddComponent<NpcEntityAnimator>();   // 必须在设好控制器之后加：Awake 会缓存 Animator

                driver.ApplyState((byte)NpcAnimationState.Run, false);
                animator.Update(0.2f);   // 跨过 0.15s 过渡再断言：过渡期间 GetCurrentAnimatorStateInfo 报哪个状态是实现细节
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Run"),
                    "写 MonsterState=2 之后必须真的进 Run —— 参数名对不上就会停在上一个状态");

                driver.PlayAttack(variant: 0);
                animator.Update(0.2f);   // 跨过 0.1s 过渡
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack01"),
                    "Trigger 名对不上就永远不播攻击");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Two_Swings_In_A_Row_Both_Play()
        {
            var go = new GameObject("TwoSwings");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller");
                var driver = go.AddComponent<NpcEntityAnimator>();
                driver.ResolveAnimator();   // EditMode 下 AddComponent 不触发 Awake

                driver.PlayAttack(variant: 0);
                animator.Update(0.2f);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack01"));

                animator.Update(1.0f);      // 推过片段（0.667s）+ exit-time 回 Idle
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "攻击态必须已退出");

                driver.PlayAttack(variant: 1);
                animator.Update(0.2f);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack02"),
                    "第二刀必须能播 —— 攻击态若没在冷却内退出，canTransitionToSelf=false 会把它静默吞掉");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
```

**这条用例守的是一个隐式不变量，必须写进两个控制器的资产注释**：

> 攻击片段时长必须**短于**该怪的冷却 —— 否则下一刀到达时攻击态还没退出，而 `canTransitionToSelf = false` 会让新 Trigger **被静默吞掉**（不报错、不播、不计数，只是丢刀）。

实测余量：DogKnight `Attack01/Attack02` **0.833s** vs 冷却 1.5s；SpiderBoss `attack1/attack2` **0.667s** vs 冷却 2.0s —— 都约 2 倍余量，当前安全。将来谁调冷却或换片段，这条用例会替他发现。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Monstor/DogKnight/Animator/NpcAnimator_DogKnight.controller* \
        Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller* \
        Assets/Scripts/Tests/Editor/SyncLayer/NpcAnimatorAssetTests.cs
git commit -m "feat(monster): two code-driven animator controllers with exit-time returns"
```

> **已知状态（2026-10-12 实测）**：`Assets/Monstor/DogKnight/` **整棵树尚未入库** —— 那是用户在途的一次资源搬迁（`Assets/3rd/DogKnight/**` 72 个删除 + 新位置未跟踪；`DogPBR.prefab`/`Attack01.anim`/`DogControl.controller` 都不在 HEAD）。所以本提交的控制器会引用**尚未提交的** `.anim` 文件。**不要顺手把那次搬迁一起提交**（那是用户自己的在途工作，且牵涉仓库级策略）；只在报告里点名"本提交引用了未跟踪的片段"。

---

## 任务 6：怪物预制体与 spider 资源导入

**文件：**
- 创建：`Assets/Monstor/fantasySpider/spider_myOldOne_Mecanim.FBX`（复制件）
- 创建：`Assets/Monstor/DogKnight/Prefab/DogKnightMonster.prefab`
- 创建：`Assets/Monstor/fantasySpider/SpiderMonster.prefab`

- [ ] **步骤 1：蜘蛛 FBX 复制并改成 Generic**

> **硬前提**（spec §4.1）：`spider_myOldOne.FBX` 的 `animationType: 1` 是 **Legacy**，legacy 片段**不能**给 AnimatorController 用。复制一份改导入设置，原资产与其演示场景保持可用。

```
MCP assets-copy '{"sourcePaths":["Assets/Monstor/fantasySpider/spider_myOldOne.FBX"],"destinationPaths":["Assets/Monstor/fantasySpider/spider_myOldOne_Mecanim.FBX"]}'
```

```
MCP script-execute '{"isMethodBody":true,"className":"SpiderRig","methodName":"Run","csharpCode":"var imp = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(\"Assets/Monstor/fantasySpider/spider_myOldOne_Mecanim.FBX\");\nimp.animationType = UnityEditor.ModelImporterAnimationType.Generic;\nimp.SaveAndReimport();\nDebug.Log(\"[SPIDER-RIG] type=\" + imp.animationType);"}'
```

验证（回读，不能只看日志）：

```
MCP script-execute '{"isMethodBody":true,"className":"SpiderRigCheck","methodName":"Run","csharpCode":"var imp = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(\"Assets/Monstor/fantasySpider/spider_myOldOne_Mecanim.FBX\");\nDebug.Log(\"[SPIDER-RIG-CHECK] type=\" + imp.animationType);\nforeach (var a in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(\"Assets/Monstor/fantasySpider/spider_myOldOne_Mecanim.FBX\")) { if (a is AnimationClip c) Debug.Log(\"[SPIDER-CLIP] \" + c.name + \" len=\" + c.length.ToString(\"F2\")); if (a is Avatar av) Debug.Log(\"[SPIDER-AVATAR] \" + av.name + \" valid=\" + av.isValid); }"}'
```

预期：`type=Generic`；13 段 clip（含 `death1`/`hit1`/`taunt`/`jump`）；一个 `isValid=True` 的 Avatar。

- [ ] **步骤 2：DogKnight 怪物预制体（Variant）**

用 `DogPBR.prefab` 派生 Variant（**不动原预制体** —— Asset Store 自带 `Scene/DogKnight/Scene/Scene01.unity` 还引用它）：

```
MCP assets-prefab-create '{"sourcePrefabAssetPath":"Assets/Monstor/DogKnight/Prefab/DogPBR.prefab","prefabAssetPath":"Assets/Monstor/DogKnight/Prefab/DogKnightMonster.prefab"}'
```

进编辑态改一处：Animator 的 `m_Controller` → `NpcAnimator_DogKnight.controller`，`Apply Root Motion` 保持 0。

> **预制体上不放 `NpcEntityAnimator`** —— 驱动由 registry 在实例化后动态 `AddComponent` 添加（与真人角色的既有做法一致：`RemoteEntityRegistry.cs:190` 对玩家预制体也是 `AddComponent<RemoteEntityAnimator>()`）。原因：registry 是普通类，项目规则禁止它在非 MonoBehaviour 上下文里 `GetComponent`；让预制体保持纯美术，职责也更干净。

> 根节点的 Animator 组件用 `gameobject-find` + `gameobject-component-modify` 把 `m_Controller` 指到新控制器（`object-modify` 亦可）。

```
MCP assets-prefab-save '{}'
MCP assets-prefab-close '{"save":true}'
```

- [ ] **步骤 3：Spider 怪物预制体**

一次性用脚本建好并保存（`PrefabUtility.SaveAsPrefabAsset` 是预制体的规范 API；**写完必须靠步骤 4 的断言回读验证** —— 本项目在 script-execute 写资产上有过不可靠的前科）：

```
MCP script-execute '{"isMethodBody":true,"className":"SpiderPrefab","methodName":"Run","csharpCode":"var fbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(\"Assets/Monstor/fantasySpider/spider_myOldOne_Mecanim.FBX\");\nvar root = new GameObject(\"SpiderMonster\");\nvar inst = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(fbx);\ninst.transform.SetParent(root.transform, false);\nroot.transform.localScale = new Vector3(2f, 2f, 2f);\nvar animator = inst.GetComponentInChildren<Animator>();\nvar legacy = inst.GetComponentInChildren<Animation>();\nif (legacy != null) Object.DestroyImmediate(legacy);\nanimator.applyRootMotion = false;\nanimator.runtimeAnimatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(\"Assets/Monstor/fantasySpider/NpcAnimator_Spider.controller\");\nanimator.gameObject.AddComponent<Hotfix.GameSystems.SyncLayer.NpcEntityAnimator>();\nUnityEditor.PrefabUtility.SaveAsPrefabAsset(root, \"Assets/Monstor/fantasySpider/SpiderMonster.prefab\");\nObject.DestroyImmediate(root);\nUnityEditor.AssetDatabase.SaveAssets();\nUnityEditor.AssetDatabase.Refresh();\nDebug.Log(\"[SPIDER-PREFAB] saved\");"}'
```

> **预制体上不放驱动组件** —— 由 registry 在实例化后 `AddComponent` 添加（见步骤 2 的说明）。上面这段只做：`localScale = (2,2,2)`（spec D6：3.3m 宽）、关根运动、去 legacy `Animation`、指控制器。

- [ ] **步骤 4：断言预制体结构（追加到 `NpcAnimatorAssetTests`）**

```csharp
        [Test]
        public void Monster_Prefabs_Carry_The_Driver_And_The_Right_Controller()
        {
            var cases = new (string Path, string Controller)[]
            {
                ("Assets/Monstor/DogKnight/Prefab/DogKnightMonster.prefab", "NpcAnimator_DogKnight"),
                ("Assets/Monstor/fantasySpider/SpiderMonster.prefab", "NpcAnimator_Spider"),
            };

            foreach (var (path, controllerName) in cases)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);

                // 预制体保持纯美术：驱动由 registry 实例化后 AddComponent（普通类不能用 GetComponent）
                Assert.IsNull(prefab!.GetComponentInChildren<NpcEntityAnimator>(true),
                    $"{path} 不该挂驱动组件——它在实例化后由 registry 添加");

                var animator = prefab.GetComponentInChildren<Animator>(true);
                Assert.IsNotNull(animator, $"{path} 缺 Animator");
                Assert.IsNotNull(animator!.runtimeAnimatorController, $"{path} 的 Animator 没有控制器");
                Assert.IsTrue(animator.runtimeAnimatorController!.name.Contains(controllerName), $"{path} 控制器不对");
                Assert.IsFalse(animator.applyRootMotion, $"{path} 必须关根运动（位置由服务端权威）");
            }
        }

        [Test]
        public void Spider_Boss_Is_Twice_Its_Imported_Size()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Monstor/fantasySpider/SpiderMonster.prefab")!;
            Assert.AreEqual(2f, prefab.transform.localScale.x, 0.001f);
        }
```

- [ ] **步骤 5：运行测试验证通过**

运行：`UTEST NpcAnimatorAssetTests`
预期：6 个用例全绿。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Monstor/fantasySpider/SpiderMonster.prefab* \
        Assets/Monstor/DogKnight/Prefab/DogKnightMonster.prefab* \
        Assets/Scripts/Tests/Editor/SyncLayer/NpcAnimatorAssetTests.cs
git commit -m "feat(monster): prefabs for the knight and the boss"
```

> **三处现实纠正（2026-10-12 实测；初稿在这几处写错了）**：
> 1. **本仓库并没有启用 Git LFS** —— 无 `.gitattributes`、`git check-attr filter` 返回 `unspecified`、`git lfs ls-files` 为 0。项目 `CLAUDE.md` 里"Files are tracked with Git LFS"这句**与事实不符**。所以**不要**去加 `*.FBX filter=lfs`：那会回溯作用于全部既存 FBX，需要 `git lfs migrate` 重写历史，属仓库级策略变更，**不在本计划范围**。沿用仓库现状（裸存）提交。
> 2. FBX 复制件 `spider_myOldOne_Mecanim.FBX` 已在**前置步骤**单独提交过 —— 本步骤不要再 `git add` 整个 `fantasySpider/` 目录（会把无关的未跟踪文件卷进来）。
> 3. `Assets/Monstor/DogKnight/` 整棵树**未入库** —— 本步骤的 Variant 会引用未提交的基预制体 `DogPBR.prefab`。**不要顺手提交那次搬迁**，只在报告里点名。
>
> 另：`LoadAllAssetsAtPath` 会连 Unity 内部的 `__preview__Take 001` 一起返回（源 FBX 同样如此）——若写"恰好 N 段片段"的断言，必须排除 `__preview__` 前缀。

---

## 任务 7：宿主接线（目录、路由、场景）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`（`CreateView` 查表、`UpdateRender` 喂驱动、`PlayAttack` 排程、`Remove`/`Clear` 清排程）
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`（`Net` 对象的两个字段，用 MCP 接线，**不手改 YAML**）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs`

- [ ] **步骤 1：写失败的测试**

`RemoteEntityRegistryTests.cs` 追加：

```csharp
    [Test]
    public void Npc_View_Uses_The_Catalog_Prefab_When_Present()
    {
        var prefab = new GameObject("FakeMonsterPrefab");
        prefab.AddComponent<Animator>();
        var root = new GameObject("CatalogRoot");
        var registry = new RemoteEntityRegistry(root.transform, null,
            new Dictionary<int, GameObject> { { 3, prefab } });
        try
        {
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2_000_002,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                X = 1f, Z = 1f, Kind = (byte)EntityKind.Npc, TemplateId = 3, Tick = 1
            }, serverTicks: 1_000_000L);

            var view = registry.GetView(2_000_002);
            Assert.IsNotNull(view);
            Assert.IsTrue(view!.GameObject.name.StartsWith("FakeMonsterPrefab"),
                $"命中目录应实例化真预制体，实际是 {view.GameObject.name}");
            Assert.IsNotNull(view.NpcAnimator, "registry 应在实例化出的怪物根上补上驱动组件");
        }
        finally
        {
            registry.Clear();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(prefab);
        }
    }

    [Test]
    public void Npc_View_Falls_Back_To_The_Sphere_When_The_Template_Is_Missing()
    {
        var root = new GameObject("FallbackRoot");
        var registry = new RemoteEntityRegistry(root.transform, null, null);
        try
        {
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2_000_003,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                X = 1f, Z = 1f, Kind = (byte)EntityKind.Npc, TemplateId = 99, Tick = 1
            }, serverTicks: 1_000_000L);

            var view = registry.GetView(2_000_003);
            Assert.IsTrue(view!.GameObject.name.Contains("Monster_"), "未命中目录要保持球体降级");
        }
        finally
        {
            registry.Clear();
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void A_Scheduled_Attack_For_A_Destroyed_View_Is_Harmless()
    {
        var root = new GameObject("DropRoot");
        var prefab = new GameObject("FakeMonsterPrefab");
        prefab.AddComponent<Animator>();
        var registry = new RemoteEntityRegistry(root.transform, null,
            new Dictionary<int, GameObject> { { 3, prefab } });
        try
        {
            registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2_000_004,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                X = 0f, Z = 0f, Kind = (byte)EntityKind.Npc, TemplateId = 3, Tick = 1
            }, serverTicks: 1_000_000L);

            registry.ScheduleAttack(2_000_004, serverTimestamp: 1_000_000L + 500L * 10_000L);
            registry.Remove(2_000_004);

            // 排程项到点时视图已不在 —— 不得抛异常，也不得把视图复活
            Assert.DoesNotThrow(() => registry.UpdateRender(
                nowServerTicks: 1_000_000L + 1_000L * 10_000L, renderDelayTicks: 0L));
            Assert.IsNull(registry.GetView(2_000_004), "排程项不得复活已销毁的视图");
        }
        finally
        {
            registry.Clear();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(prefab);
        }
    }
```

> **本条只保证"无害"**，不保证"排程项被清掉"—— 那个语义由任务 4 的 `NpcAttackScheduleTests.Clear_For_Entity_Drops_Its_Pending_Attacks` 覆盖，别在这条里断言它看不见的东西。


- [ ] **步骤 2：运行测试验证失败**

运行：`UTEST RemoteEntityRegistryTests`
预期：编译错误 —— 构造函数不接受目录参数。

- [ ] **步骤 3：registry 查表建视图 + 攻击排程**

`RemoteEntityRegistry.cs`：

```csharp
        private readonly IReadOnlyDictionary<int, GameObject>? _npcPrefabs;
        private readonly NpcAttackSchedule _attacks = new NpcAttackSchedule();
        private readonly List<NpcAttackSchedule.DueAttack> _dueAttacks = new List<NpcAttackSchedule.DueAttack>();

        public RemoteEntityRegistry(Transform root, GameObject? playerAvatarPrefab = null,
            IReadOnlyDictionary<int, GameObject>? npcPrefabs = null)
        {
            _root = root;
            _playerAvatarPrefab = playerAvatarPrefab;
            _npcPrefabs = npcPrefabs;
        }
```

`CreateView` 的 Npc 分支：

```csharp
            if (snapshot.Kind == (byte)EntityKind.Npc)
            {
                if (_npcPrefabs != null && _npcPrefabs.TryGetValue(snapshot.TemplateId, out var prefab) && prefab != null)
                {
                    var model = Object.Instantiate(prefab, _root, false);
                    model.name = $"Monster_{playerId}_T{snapshot.TemplateId}";
                    // AddComponent 同步跑 Awake（与真人路径同惯例，见本文件 :190 对玩家预制体的处理）——
                    // 驱动在 Awake 里用 GetComponentInChildren 找到模型自带的 Animator。
                    // 本类是普通类，项目规则禁止 GetComponent，所以驱动只能这样加。
                    var driver = model.AddComponent<NpcEntityAnimator>();
                    return new RemoteEntityView(model, snapshot.Kind, snapshot.TemplateId, npcAnimator: driver);
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);   // 降级占位（目录未命中/未接线）
                go.name = $"Monster_{playerId}_T{snapshot.TemplateId}";
                go.transform.SetParent(_root, false);
                return new RemoteEntityView(go, snapshot.Kind, snapshot.TemplateId);
            }
```

`UpdateRender` 里喂状态与排程：

```csharp
        public void UpdateRender(long nowServerTicks, long renderDelayTicks)
        {
            long renderAt = nowServerTicks - renderDelayTicks;

            // 攻击排程先于状态写入。**别把理由写成"否则 locomotion 会打断挥砍"**（2026-10-12 复审纠正）：
            // 驱动的忙位在 PlayAttack 里当场置位，同帧内两种顺序的终值相同，locomotion 情形不分叉。
            // 真正分叉的只有**同帧采到 Hit/Death**：先排程则命中/死亡那笔最后落盘（并顺手释放忙位），
            // 顺序反了则挥砍值最后落盘、命中/死亡要等下一帧才夺回 —— 差 1 帧。之所以选先排程，
            // 是因为驱动注释里"Hit/Death 分支是唯一兜底"想要的正是前者。
            _dueAttacks.Clear();
            _attacks.Drain(renderAt, _dueAttacks);
            foreach (var due in _dueAttacks)
                if (_views.TryGetValue(due.EntityId, out var attacker))
                    attacker.NpcAnimator?.PlayAttack(due.Variant);

            foreach (var kv in _views)
            {
                if (_interpolator.TryGetRenderState(kv.Key, nowServerTicks, renderDelayTicks,
                        out var pos, out var rot, out var actionState, out var dead))
                {
                    kv.Value.Position = pos;
                    kv.Value.Rotation = rot;
                }

                if (kv.Value.NpcAnimator != null
                    && _interpolator.TryGetSampleState(kv.Key, renderAt, out var sampled, out var sampledDead))
                    kv.Value.NpcAnimator.ApplyState(sampled, sampledDead);
            }
        }
```

新增攻击入口（供宿主路由）：

```csharp
        /// <summary>服务端裁定的一次挥砍（CastBroadcast）——排到该时间戳对应的渲染时刻再播。</summary>
        public void ScheduleAttack(long casterId, long serverTimestamp)
        {
            if (!_views.TryGetValue(casterId, out var view) || view.Kind != (byte)EntityKind.Npc) return;
            if (view.NpcAnimator == null) return;   // 球体降级：没有可驱动的东西
            _attacks.Schedule(casterId, serverTimestamp);
        }
```

`Remove(long)` 与 `Clear()` 里加 `_attacks.Clear(entityId)` / `_attacks.ClearAll()`。

- [ ] **步骤 4：宿主接线**

`ClientNetworkHost.cs`：

```csharp
        [Serializable]
        public class NpcPrefabEntry
        {
            public int TemplateId;
            public GameObject Prefab;
        }

        [Header("怪物模型（templateId → prefab；留空则该 template 退化为球体占位）")]
        [SerializeField] private NpcPrefabEntry[] _npcPrefabs = Array.Empty<NpcPrefabEntry>();
```

`Awake()` 里建表并交给 registry：

```csharp
            var catalog = new Dictionary<int, GameObject>();
            foreach (var entry in _npcPrefabs)
                if (entry != null && entry.Prefab != null)
                    catalog[entry.TemplateId] = entry.Prefab;

            _remoteEntities = new RemoteEntityRegistry(root.transform, _playerAvatarPrefab,
                catalog.Count > 0 ? catalog : null);
```

广播路由（把既有的日志 lambda 扩展成日志 + 路由）：

```csharp
            _combatBridge.OnCastBroadcast += b =>
            {
                _remoteEntities!.ScheduleAttack(b.CasterPlayerId, b.ServerTimestamp);
            };
```

> 既有那行 `Debug.Log($"[NetHost] cast broadcast: ...")` 保留与否由实现者定；**不要**为它新增分支。

- [ ] **步骤 5：运行测试验证通过**

运行：`UTEST RemoteEntityRegistryTests`
预期：全绿（含既有挂起/超时用例）。

- [ ] **步骤 6：场景接线（MCP，不手改 YAML）**

DemoDay 的 `Net` 对象上把 `_npcPrefabs` 填成两条：`3 → DogKnightMonster.prefab`、`4 → SpiderMonster.prefab`。

```
MCP gameobject-find '{"gameObjectRef":{"instanceID":0,"name":"Net"},"includeComponents":true}'
```

拿到 `Net` 的 `ClientNetworkHost` 组件实例 id 后：

```
MCP gameobject-component-modify '{"gameObjectRef":{"instanceID":0,"name":"Net"},"componentRef":{"instanceID":0,"typeName":"Hotfix.GameSystems.SyncLayer.ClientNetworkHost"},"jsonPatch":{"_npcPrefabs":[{"TemplateId":3,"Prefab":{"instanceID":<DogKnightMonster 预制体 instanceID>}},{"TemplateId":4,"Prefab":{"instanceID":<SpiderMonster 预制体 instanceID>}}]}}'
```

然后 `MCP scene-save '{"openedSceneName":"DemoDay"}'`。

验证：`MCP scene-get-data '{"openedSceneName":"DemoDay","paths":["<Net 的组件路径>/_npcPrefabs"]}'` 回读两条非空；或直接看 `gameobject-component-get` 的字段。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/RemoteEntityRegistryTests.cs \
        Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat(sync): npc views resolve real monster prefabs and route cast broadcasts"
```

---

## 任务 8：部署收尾与端到端回归

**文件：**
- 修改：`production/deploy/webgl-demo/start-game-server.cmd`
- 同步：`production/deploy/webgl-demo/dist/server/Config/monster_config.json`

- [ ] **步骤 1：演示启动脚本**

`production/deploy/webgl-demo/start-game-server.cmd`：`--monsters 2` → `--monsters 6`，并把那条注释改成反映现状。

> 注释内容**必须按实测写**，不能照抄旧推导。已知事实：DemoDay 出生点 **(10, 0, -15.5)**；两个新点位须满足"到出生点距离 > 感知 + 巡逻"（DogKnight ≥ 28m、SpiderBoss ≥ 41m）。初稿写的"距原点 20m/42m、开局不仇恨"是错的 —— 那是按原点推的，而原点只是 e2e 测试观察者的位置。

- [ ] **步骤 2：同步部署副本**

```
bash production/deploy/webgl-demo/publish-server.sh
```

或直接同步那一份 json（`dist/server/Config/monster_config.json` 是独立副本，与源文件当前一致）。验证：`diff production/deploy/webgl-demo/dist/server/Config/monster_config.json Server/Config/monster_config.json` 应无输出。

- [ ] **步骤 3：服务端全量回归**

运行：`dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q`（不带 `--filter` 即跑全部）—— 预期全绿。
运行：`dotnet build Server/Server3C.csproj --nologo -v q` —— 预期 0 error。

- [ ] **步骤 4：Unity 全量回归**

运行：`MCP tests-run '{"testMode":"EditMode"}'` —— 预期全绿（当前基线 293）。

PlayMode e2e 需要**先起一个 `--monsters 4` 的服务器**（`MonsterWorldTests` 依赖固定布局的前四点位）：

```bash
dotnet build Server/Server3C.csproj --nologo -v q
dotnet run --no-build --project Server/Server3C.csproj -- 8000 --monsters 4 > /tmp/srv-monsters4.log 2>&1 &
```

然后跑 `MCP tests-run '{"testMode":"PlayMode","testClass":"MonsterWorldTests","timeout":600000}'`。
预期 PASS，且日志里的模板序列仍是 `2,1,1,2`（前四点位未被改动）。
**跑完必须停服**：`netstat -ano | grep ":8000" | grep LISTENING` 取 PID，`taskkill //F //PID <pid>`。

- [ ] **步骤 5：起 6 只怪的服务器，人工截图**

```bash
dotnet run --no-build --project Server/Server3C.csproj -- 8000 --monsters 6 > /tmp/srv-monsters6.log 2>&1 &
```

进 PlayMode（`MCP editor-application-set-state '{"IsPlaying":true}'`），等客户端连上（连接状态浮层 `_showDebugOverlay` 会写模式/状态/实体数；也可用 `console-get-logs` 看 `[NetHost]` 行）。

两只怪距出生点约 **46m** 与 **51m**，而观察者视野 80m —— 所以在出生点就能看见，**不必走过去**（但比初稿预期的更远，画面里会偏小；要看细节可走过去）。若默认机位没框住它们，用 `script-execute` **调整相机朝向**（不要伪造结果、不要直接摆怪），再：

```
MCP screenshot-game-view '{}'
```

预期：骑士（约 1.1m 高、剑盾）与蜘蛛 Boss（约 3.3m 宽）是**真模型而非球体**；两者都在播动画（连续截两张可对比姿态变化）；Boss 体型明显大于骑士。
把截图存到 `production/verify/`（既有惯例，命名带 `t8` 前缀，例如 `production/verify/t8_monsters.png`），并在本计划末尾的验收记录里写一行结论。**跑完同样要停服。**

- [ ] **步骤 6：Commit**

```bash
git add production/deploy/webgl-demo/start-game-server.cmd \
        production/verify/t8_monsters.png
git commit -m "chore(demo): the deployed demo spawns six monsters, config copy refreshed"
```

> `production/verify/` 下已有大量历史产物 —— 只 add **本次新产生**的截图，不要 `git add` 整个目录。
>
> ⚠️ **`production/deploy/webgl-demo/dist/` 被 `.gitignore:88` 忽略**（2026-10-12 实测）—— 初稿把 `dist/server/Config/monster_config.json` 列进 `git add` 是**做不到的**（除非 `-f` 强加，而那会破坏该目录被忽略的设计）。所以"同步部署副本"是**工作区操作**（跑 `publish-server.sh` 或直接同步文件），不入库；真正的部署件由发布流程在部署时重生成。

---

## 验收记录（执行时回填）

| 任务 | 结果 | 备注 |
|---|---|---|
| 1 | 完成（含 2 轮修复） | 模板/技能/布局落地；**点位推导用错参照点**（按"距原点"推，而出生点是 (10,0,-15.52)）→ 修正为距出生点 46.5m/50.9m + 几何与行为两条守卫；文档口径四处收口 |
| 2 | 完成（含 1 轮修复） | 状态翻转即发；修复了 `ServerClock` 注释里"唯一出口"的假陈述 |
| 3 | 完成（含 1 轮修复） | 快照缓冲携带状态、按 `renderAt` 阶梯采样；修复了两个查询入口在区间右端点的分叉 |
| 4 | 完成（含 1 轮修复） | 驱动 + 排程；**修复了忙位永久锁存**（会冻结怪物一切后续动画）与变体"必然交替"的假命题 |
| 5 | 完成（含 1 轮修复） | 两个控制器；**修复了 Critical：`Attack` 参数只活在 Unity 内存里、磁盘字节缺失**（8 个断言假绿） |
| 6 | 完成（含前置 6a） | 两个预制体；6a 含 FBX 复制 + Generic + `idle/walk/run` 循环修复 |
| 7 | 完成 | 宿主接线；命名与简报自相矛盾处按"测试逐字过 + 保身份"处置 |
| 8 | 完成（含最终收口） | 部署收尾 + 全量回归 + 截图；**"开局不仇恨"被实测推翻**（真凶是既有 [0] Wolf，见下）；最终审查后重拍了最终点位的截图 |
| 最终审查 | 3 Important + 5 Minor → 一次收口修复 | 见 `progress.md`：I1 受击态残留（骑士受击动画循环）、I2 挥砍链无用例（spec §8#4 被丢）、I3 截图早于点位冻结 |

> **已知限制（合并门禁请照读）**：① `Assets/Monstor/DogKnight/` 整棵树**未入库**（用户在途搬迁）⇒ **干净 clone 上 `NpcAnimatorAssetTests` 的若干用例必然红**，本分支的绿是"在用户工作区状态下"取得的；② `[0] Wolf` 在出生点 5.9m 处（感知 15m）⇒ **任何 `--monsters ≥ 1` 下挂机都会被它打死**（既有设计 + e2e 确定性依赖，本次未动）；③ 玩家可穿过 Boss（服务端无碰撞）；④ 本仓库未启用 Git LFS，`dist/` 被 gitignore。

## 与 spec 的两处有意偏离

1. **预制作目录不用 ScriptableObject**（spec §4 写的是 `NpcPrefabCatalog.asset`）。改用 `ClientNetworkHost` 上的 `SerializedArray (templateId, prefab)`。原因：场景接线无论如何都要做一次，SO 换来的只是"以后加怪不开场景"这点便利，却要引入 `script-execute` 创建资产的路径 —— 那条路在本项目有"资产反序列化不正确"的前科（见项目记忆 `unity-mcp-script-execute-asset-trap`）。
2. **spec §8 第 10 条（`AoiBroadcasterTests`）由任务 2 的第三个调度器用例覆盖**。广播器只是把 `ActionState` 透传给 `ShouldSend`，加参数后**编译期即强制接线**；再写一个需要构造 AOI 世界的集成用例，成本高于它挡住的回归面。

## Backlog（执行账本删除前的逐字摘录）

> 账本本身不入库，删工作区前把这条清单捞进来。**以下为逐字摘录、未再分类**（保留原始措辞以免二次转述走样）。最终审查当年对它们的甄别结论是：
>
> - **必须先修**（影响行为 / 让验证失真 / 守关键不变量）：I1 受击态残留、I2 挥砍链无用例、I3 截图早于点位冻结、M1–M5 —— **全部已修**。
> - **已被后续提交超期解决**（标 closed-superseded）：p5 布局表缺 [4]/[5]、boundaries/runbook 的"调小数量就安全"、"`AttackVariant` 写入无断言"、boundaries 的"演示用 1 或 2"。
> - **其余归 backlog**：测试深度类与"未来护栏"类（当前不可达或有界）。
> - **不修**（已知限制）：见上面的验收记录。
>
> ⚠️ 摘录里少数条目（例如"截图只在工作区未跟踪"、"cmd 的 7 秒口径"）**当时记为待办，其后已被修复**；下文保留原文只为留痕。

 - Task 1: minor (deferred): SkillIdOf 提 public 属计划强制（审查者已核实影响面为零：仅 MonsterModule.cs:97 与测试引用，Server/ 不被 Unity asmdef 引用）
 - Task 1: minor (deferred): New_Templates_Can_Spawn 只断言 TemplateId/npcId，未断言 MaxHp —— 日后改错 t4 的 1500 无测试报警
 - Task 1: minor (deferred): CreateDefaultConfig 与 monster_config.json 的同步无测试守护，只靠"照抄"
 - Task 1: minor (deferred): attackRange 的 InlineData 硬编码 1..4，日后加模板 5 会静默不覆盖
 - Task 1: minor (deferred): 测试命名/读盘依赖与 .claude/rules/test-standards.md 不一致（既有服务端测试惯例如此，非本次引入）
 - Task 2: minor (deferred): 翻转分支保留旧位置锚点，同 tick 位移时下一 tick 可能多补一帧（有界，报告已披露）
 - Task 2: minor (deferred): 用例 3 未复现"保活相位不同"的原场景（两位观察者同在 tick 100 首帧）；同 tick 位移无覆盖
 - Task 2: minor (deferred): new Slot { } 字面量已 3 处，将来加字段易漏一处（沿用既有写法）
 - Task 2: minor (deferred): 翻转判定完全绕过带间隔，若 ActionState 来源每 tick 抖动会让远带退化为每 tick 全字段（无护栏、无上限测试）
 - Task 2: minor (deferred): scope-out 观察 —— NpcSpawn/NpcDespawn 带 ServerTimestamp 且已在 MessageCodec 注册，但服务端无发送点、客户端无消费者（未接线的协议面）
 - Task 2: minor (deferred): Assets/Scripts/Examples/ServerExample.cs:145/197 仍直写墙钟（Unity 侧示例，不在 Server/ 工程内）
 - Task 4: minor (deferred): AttackVariant 的写入无任何断言（变体写错参数不会被发现）
 - Task 4: minor (deferred): 两条 Animator 用例的判读依赖未文档化的 AnyState 评估顺序（先注册者先赢）
 - Task 4: minor (deferred): ResolveAnimator 只在 Awake 调，而 Write 在 _animator==null 时仍推进 _lastWritten → 事后补解析也补不回当前状态（当前预制体同步实例化，风险窄）
 - Task 4: minor (deferred): Drain 同帧多项的"时间戳升序"分支无用例覆盖（现有用例每次最多追加 1 项，>1 短路）—— 恰是本次修复触碰的分支
 - Task 4: minor (deferred): ClearAll() 无用例
 - Task 4: minor (deferred): NpcEntityAnimator.cs:30 的 Animator? 触发 CS8632（项目未开 nullable 上下文，与 RemoteEntityAnimator 同款）
 - Task 3: minor (deferred): RemoteEntityRegistry.TrySampleNpcState 的 summary 写"供 UpdateRender 与测试使用"，但 UpdateRender 并未调用它（任务 7 才接）—— 现在时会误导
 - Task 3: minor (deferred): RemoteEntityRegistry.cs:52-54 的"无条件覆盖安全前提"注释只点名 view.ActionState/view.Dead，没点名本任务新增的第二个义务点（:71-73 采样实参），服务端全掩码现状已核实支持该前提
 - Task 3: minor (deferred): 用例名 Npc_View_Keeps_Sampled_State_Instead_Of_Arrival_State 过度承诺（单样本下"采样"与"到达"恒等，它实际钉的是生产接线）
 - Task 3: minor (deferred): 报告 §2.1/§4 的用例计数口径不一致（5 vs 6，实际 6）
 - Task 3: minor (deferred, from re-review): 重复时间戳样本时位置取先入样、状态取后入样（亚 tick，需 renderAt 精确落在该 tick；类注释已声明有序性归外部保证）
 - Task 3: minor (deferred, from re-review): "最旧 fallback"用例用了 2 样本而非裁定字面的 3 个（鉴别力不受实质影响）
 - Task 5: minor (deferred, from re-review): m_HasFixedDuration 未纳入断言（关掉后数值仍留 0.15 但语义变成"源状态时长的 15%"，断言不会红）
 - Task 5: minor (deferred, from re-review): AttackClipLength 的 (AnimationClip)st.motion 强制转换——将来挂 BlendTree 会抛异常而非干净的断言失败
 - Task 6a: minor (deferred): 循环守护用例只报每个控制器的第一个违规态（诊断粒度粗）
 - Task 6a: minor (deferred): internalID: 0 → 显式 74xxxxx 的副作用经复审确认无损（固化值 = 控制器早已引用的值；7 条引用逐名比对）
 - Task 6: minor (deferred): 控制器名比对用 name.Contains（当前两名字互不为子串，够用；将来出现 NpcAnimator_Spider_Alt 之类会误绿）→ 宜改 AreEqual 或比 GUID
 - Task 6: minor (deferred): 缩放只断言 localScale.x（只改 y/z 会漏）
 - Task 6: minor (deferred): 无断言钉住 DogKnightMonster 是 Variant；无 legacy Animation 的显式断言
 - Task 6: minor (deferred): Variant 含 10 条恒等 transform 覆盖（SaveAsPrefabAsset 的标准产物），会把根 transform 钉死
 - Task 7: minor (deferred): 真模型命名 {prefab.name}_{id}_T{t} 与降级球体 Monster_... 形式不一致；降级用例的 Contains("Monster_") 判别力偏弱（简报逐字给定）
 - Task 7: minor (deferred): 未命中目录的降级用例只覆盖 null 目录，未覆盖"目录非空但 templateId 缺失"（代码正确，属计划内测试范围偏窄）
 - Task 7: minor (deferred): 广播早于首帧快照时第一刀被丢弃（计划强制：只对已存在视图排程）
 - Task 1: minor (deferred): 几何守卫只覆盖 [4]/[5]（[0] 按设计违反该规则），InlineData 写死 4/5 → 将来加槽位不会自动覆盖
 - Task 1: minor (deferred): p5-monster-server.md 的固定布局表缺 [4]/[5]（标注属 T5 同步范围）
 - Task 8: minor (deferred): 最强的视觉证据（两张隔离渲染 + 两张 3× 裁切）只在工作区未跟踪 → 建议一并入库，否则工作区被清理后只剩报告里的探针文本
 - Task 8: minor (deferred): boundaries.md:133 的"演示用 1 或 2"与脚本的 --monsters 6 未统一（已并入文档收口修复）
 - Task 1: minor (deferred, 待最终审查一并甄别): production/deploy/webgl-demo/start-game-server.cmd:19 仍写"约 7 秒阵亡"、:23 仍写"或按需把数量调低" —— 与 a976be9 新立的口径相抵（该文件是演示者实际双击运行的那个）
 - Task 1: minor (deferred): 实现者"残留只在 .superpowers"的自述被高估 —— production/verify/t9、t12 的历史脚本里也有"约 7 秒"文本（历史产物、非用户可见，对当时状态为真）

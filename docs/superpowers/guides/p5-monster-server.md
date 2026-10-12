# P5 怪物服务端化指南（MonsterModule）

> **它是什么**：怪物世界服务端权威化——AI 巡逻/仇恨追击/攻击在服务器主循环（15Hz）驱动，怪物 HP/死亡/重生由服务器裁决，玩家断线后怪物世界持续运转。客户端只做快照渲染（Npc 球体占位），本地 `MonsterSpawner` 在联网装配成功后自动禁用。P5 删除了从未接线的旧 `Server/AI/`（05-14 遗留、自带线程/Task.Run、违反 R2 主循环单线程约定），新实现为 `Server/Monster/`（tick 驱动行为树）。

## 启动怪物模式

```bash
# 先构建（--monsters 需在 StartAsync 前注册；R2 主循环独占）
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj
dotnet run --project Server/Server3C.csproj -- 8000 --monsters 4
```

- `--monsters N`：以固定布局生成 N 只怪物（N 超过布局长度时循环复用槽位）
- 配置 `Server/Config/monster_config.json` 随构建复制到输出目录（csproj `CopyToOutputDirectory`），运行时按 `AppContext.BaseDirectory/Config` 解析——`dotnet run`/`dotnet test` 的 CWD 不可靠，勿用相对路径
- 省略 `--monsters` = 纯玩家世界（P1-P4 行为不变）

## 固定布局表（e2e 契约）

`MonsterModule.SpawnDefaultLayout` 源码注释同表——MonsterWorldTests 的确定性依赖此布局，改动须同步测试：

| index | 模板 | 位置 | 朝向 | 行为 |
|---|---|---|---|---|
| [0] | Wolf(2) | (12,0,0) | 面向原点 | 开局感知原点观察者（检测 15m ≥ 12m）→ 追击攻击；距玩家出生点 40.1m > 15+10 |
| [1] | Slime(1) | (40,0,0) | 面向原点 | 巡逻可视（观察者视野 80m）、不感知（检测 8m） |
| [2] | Slime(1) | (-40,0,0) | 面向原点 | 同上 |
| [3] | Wolf(2) | (0,0,40) | 面向原点 | 可视不感知（观察者视野 80m 内，距离 40m > 15m） |
| [4] | DogKnight(3) | (-20,0,20) | 面向原点 | 可视（距原点 28.3m < 80m）且对原点观察者不感知（28.3m > 检测 18m）；**按玩家出生点避让**：距出生点 67.1m > 感知 18m + 巡逻 6m |
| [5] | SpiderBoss(4) | (-40,0,-25) | 面向原点 | 可视（距原点 47.2m < 80m）且对原点观察者不感知（47.2m > 检测 25m）；距出生点 52.2m > 感知 25m + 巡逻 12m |

> 避让约束是**到玩家出生点 (10,0,-40)** 的距离 > 感知半径 + 巡逻半径（DemoDay 的
> `PlayerSpawner._spawnPoint` 实测；原点只是 e2e 观察者的位置，按原点推会漏判）。
> 2026-10-13 出生点从 (10,0,-15.52) 搬到湖东岸 (10,0,-40)，**六个槽位全部满足该约束**
>（旧点下 [0] Wolf 是唯一按设计违反规则的槽位：巡逻圈最近切到 5.9m < 检测 15m，挂机玩家是被它打死的）。
> 守备测试：`MonsterModuleTests.New_Layout_Slots_Stay_Out_Of_Reach_Of_Demo_Spawn`（全六槽位，几何）与
> `Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_Any_Monster`（3 分钟挂机零施法，行为）。

## 怪物生命周期

生成（`NpcIdSeed` 2_000_000 起，BotRunner 占 1_000_000+）→ 巡逻（围绕巡逻中心 4 点，半径随模板）/ 感知（视野锥内**最近**存活已登录玩家；目标丢失才切换）→ 追击 → 攻击（CombatRules 技能 50001 Slime cd 1s / 50002 Wolf cd 1s / 50003 DogKnight cd 1.5s / 50004 SpiderBoss cd 2s）→ 受击硬直 5 tick（跳过行为树）→ 死亡展示 30 tick（快照持续广播死亡态）→ 移除实体（Server3C 清 AOI）→ 450 tick（30s）后以**新 id 满血**重生在巡逻中心。

死亡/重生全在服务器；断线不中断——状态全在 EntityStore + MonsterModule，零 per-session 依赖（e2e 重连断言同 id 怪物仍在）。

## 实体模型与同步链

怪物 = `PlayerState` + `Kind=1`(Npc)/`TemplateId` 入 EntityStore——AOI 视野/分级节拍/快照广播/客户端缺席超时销毁全链零改动复用。快照 `ActionState` = `NpcAnimationState`（Idle/Run/Attack/Hit/Death；玩家恒 0）。怪物无 session、无登录。

## 战斗链路

- **玩家打怪**：`CastRequest` → 服务端候选集扩展（玩家施法候选 = 玩家 + 怪物）→ `HitResolver` → `DamageResult` → 客户端怪物视图浮字（本地玩家仍走 `ApplyServerDamage`）
- **怪物打玩家**：AI Attack 动作 → `QueueCast`（显式仇恨目标 `TargetPlayerId`）→ `CombatResolver` 裁决（存活/冷却/形状/命中/掷骰）→ `CastBroadcast` 给怪物 AOI 视野观察者 + `DamageResult` 给每个受击玩家 → 客户端 `ApplyServerDamage` 走既有 HitFSM/浮字链
- 候选集策略（D-P5-3）：怪物施法只打已登录存活玩家（怪物互不打、bot 压测实体不可被命中）；玩家挥击按"锥内最近者"自然命中怪物

## 客户端

- 联网装配成功后 `ClientNetworkHost` 自动禁用场景内全部本地 `MonsterSpawner`（DemoDay 场景有两个）
- Npc 视图 = 真模型 + 动画驱动（`NpcEntityAnimator`；目录未命中/未接线时退化为球体占位），名称带预制体名前缀
- **怪物视图带运行时碰撞体**（`NpcBodyCollider`，2026-10-13）：尺寸按模型包围盒推（半径 = XZ 跨度较大者的一半，
  高度 = Y 跨度），层 `NpcBody`，层矩阵只与本地玩家层碰撞 ⇒ 撞不过去。**纯客户端判定**——服务端不模拟
  玩家移动也不校验重叠，改客户端仍可穿模（见 `webgl-demo-boundaries.md` 第 ⑪ 条）

## 扩展点：加一种新怪物

1. `Server/Config/monster_config.json` 加条目（templateId/name/hp/moveSpeed/detectionRadius/visionAngle/attackRange/patrolRadius）
2. `CombatRules` 加技能行（共享源码，双端跑表测试）
3. `MonsterModule.SkillIdOf` 加 templateId → skillId 映射
4. 客户端表现（当前球体占位自动生效，模板区分色/真实模型留 backlog）

## 验收方式

```bash
# 起服（怪物模式）
dotnet run --project E:/CodeForJob/Cool/Server/Server3C.csproj -- 8000 --monsters 4
# PlayMode（需先起服）MonsterWorldTests 2 用例：服务端驱动巡逻/追击/攻击 + 重连世界持久
unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"MonsterWorldTests"}' --timeout 300000
# 审计日志：npc cast rejected = 0（怪物施法全部通过裁决）
grep -c "npc cast rejected" production/verify/monster-audit.log
```

## 已知边界（P5 backlog）

- 玩家死亡/复活未服务端化；投射物等玩家技能未服务端化
- 仇恨 = 感知内最近玩家（AggroTable 仇恨伤害排名留 backlog）
- 怪物真实模型/动画表现（含模板区分色）；快照血条 MaxHp 未消费
- 客户端真实目标 id 判别未接入（TargetPlayerId 仍为 0 语义）
- 旧 `MonsterSpawner` 组件仍在场景（仅禁用）——名牌残影/PhysRegistry 清理留后续

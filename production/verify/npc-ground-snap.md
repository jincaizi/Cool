# Npc 视图落地吸附（远端怪不再埋在 y=0）

日期：2026-10-17 ｜ 场景：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`（打开的那张就是它）
｜ 服务端：`--monsters 4 --bots 40 --bots-radius 60`（8000 端口，**本次零服务端改动**，未重启用）
｜ 手段：`unity-mcp-cli run-tool`（assets-refresh / tests-run / script-execute / editor-application-set-state）

## 0. TL;DR

| # | 事项 | 结果 |
|---|---|---|
| 1 | 服务端布局是 2D 的（y≡0），DemoDay 地面在这些点位 −0.113 ~ +0.624m ⇒ 怪埋进地里 | **已修**：客户端只对 `EntityKind.Npc` 的视图把 y 换成地形采样值 |
| 2 | 裸射线会先打到树/石头 | **已证**：`(-25,-15)`→`Tree2` y=6.004（地形 0.309）、`(20,-20)`→`Tree1` y=3.149（地形 0.082） |
| 3 | 层掩码"只认地形" | **做不到**：地形与树/石头**同在 Default**（实测 layer=0），掩码只作粗筛；精筛是**命中体身份** |
| 4 | 编辑态 | **391/391 绿**（386 基线 + 5 条新增），0 失败 |
| 5 | 实机探针（Play 模式，3 轮 × 4 只怪 = 12 个读数） | **每只怪 \|视图根 y − 地面 y\| ≤ 0.0045**（验收线 0.01） |

---

## 1. 改了什么（4 个文件，代码净增 153 行，**无删除**）

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs` | ① `UpdateRender` 里对 Npc 视图把 `pos.y` 换成地形采样（写在 `TrackSpeed` **之后**，速度反推仍吃插值器原始位置）；② 新增 `GroundY` / `TrySampleTerrain` / `IsTerrainHit` / `GroundSample`；③ 采样游标随视图在 `Remove`/`Clear` 里清理 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 新增 `[SerializeField] private LayerMask _npcGroundMask = 1;`（Inspector 旋钮）并在 `Awake` 里 `_remoteEntities.GroundMask = _npcGroundMask;` |
| `Assets/Scripts/Tests/Editor/SyncLayer/NpcGroundSnapTests.cs`（新） | 5 条 EditMode 用例（见 §4） |
| `production/verify/npc-ground-snap.md` | 本报告 |

**没动**：插值器、速度反推、分档（Full/Proxy）、`NpcBodyCollider` 生成、任何服务端代码、任何场景/预制体/层资产。

应用点（`RemoteEntityRegistry.UpdateRender`，约 `:195`）：

```csharp
kv.Value.Position = pos;
kv.Value.Rotation = rot;
kv.Value.TrackSpeed(pos, renderAt, SpeedClamp);   // 速度反推吃**原始**插值位置（一行不动）

if (kv.Value.Kind == (byte)EntityKind.Npc)        // 真人/bot 不碰：客户端权威，y 由本地物理决定
{
    var snapped = kv.Value.Position;
    snapped.y = GroundY(kv.Key, pos.x, pos.z, pos.y);
    kv.Value.Position = snapped;
}
```

## 2. 掩码设计：为什么"只认地形"做不到只靠掩码（实测）

编辑态探针（`script-execute`，主线程，只读）：

```
[P] scene=DemoDay path=Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
[P] found LowpolyTerrain layer=0 (Default) col=MeshCollider activeInHierarchy=True
[P] found Tree1 layer=0 (Default) col=CapsuleCollider activeInHierarchy=True
[P] found Tree2 layer=0 (Default) col=CapsuleCollider activeInHierarchy=True
[P] found Rock1 layer=0 (Default) col=MeshCollider activeInHierarchy=True
[P] camera Main Camera mask=-1 (0xFFFFFFFF)      [P] dir light Directional Light mask=-1
[P] layer 11 name='' layer 3 name=''
```

- **项目里没有"地形层"**：TagManager 只有 `Default…UI` + 第 10 层 `NpcBody`；地形、树、石头**全在 Default(0)**
  （玩家 prefab 的 `GroundLayer` 也是 `m_Bits: 1` —— 项目惯例里的"地面 = Default"）。掩码 `1<<0` 挡不住树。
- 因此掩码只作**粗筛**（也确实是宿主可覆盖的 `[SerializeField]` 旋钮，默认 `1`，注释写明"不加掩码会吸到树冠上"），
  真正的精筛是**命中体身份**：`Collider` 必须是"按对象名 `LowpolyTerrain` 解析出的地形根"上的那个。
  **不用 GetComponent**（本类是普通类，项目规则禁止）——地形对象用 `GameObject.Find` 解析一次，
  碰撞体引用**来自物理查询本身**（`RaycastHit.collider`），之后按引用比对。
- 为什么不是按碰撞体类型筛：石头/树桩/大蘑菇也是 `MeshCollider`（实测 12 个 Rock + TreeStump + MushroomLarge），
  只有"身份"分得开。
- 射线路数：快路径 = 掩码射线命中地形本身（无遮挡时 1 条）；被树冠/石头截胡时 = `RaycastAll` 按身份挑地形，
  取最近的一条（网面可能被穿过一次以上）。起点 y=200、长 400，未命中回退**原 y**（不是 0）。

## 3. 采样缓存：任务建议的"0.5~1m 网格"被**实测否掉**了

地形静态 ⇒ 任务建议按 0.5~1m 网格缓存（`Dictionary<long,float>`，key 用 round(x)/round(z)）。**实测不行**：

| 方案 | 实机最大偏差（同一 XZ 与射线真值比） | 结论 |
|---|---|---|
| 0.5m 网格 | **0.0159** | 超过 1cm 验收线 |
| 0.1m 网格 | **0.0311** | 更差 |
| **按实体游标 + 1cm 复用半径（最终）** | **≤ 0.0045** | ✓ |

原因已定位（探针 #2）：湖东岸堤坡**实测坡度 ~49%**（该点 ±5cm 落差 4.9cm、+50cm 落差 24.5cm），
粗网格在同一格里复用旧采样 ⇒ 误差 = 坡度 × 格内漂移，0.1m 格就能到 3cm。

最终设计：**每实体一份游标**（`Dictionary<long, GroundSample>`：上次采样的 XZ + 结果；命中与否都记），
挪动超过 `GroundSampleRadius = 0.01m` 才重采。性质：

- 残差 ≤ 坡度 × 半径×√2 = 0.49 × 0.0141 ≈ **7mm** ✓；
- 静止的怪（含插值抖动 < 1cm）**零射线**，成本 ∝ **走过的距离**（与帧率无关）——仍然是"不要每帧每实体一次射线"；
- 游标随视图在 `Remove`/`Clear` 里清理（与 `_lastKnownYaw`/`_lastSampleTicks` 同惯例，防重连同 ID 残留）；
- 未命中（无地形 / 地形范围外）：**回退服务端 y**，`LogWarning` 但**只打一次**（`_warnedNoGround`）——测试宿主/探针
  场景没有 `LowpolyTerrain` 也照样跑，不抛异常。

## 4. EditMode（命令 + 数字）

```
unity-mcp-cli run-tool assets-refresh --input '{"options":"ForceSynchronousImport"}'   # 无编译错误
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode"}' --timeout 600000
  → Status=Passed  TotalTests=391  PassedTests=391  FailedTests=0  (5.9s)
```

**391 = 386 基线 + 5 条新增**（`NpcGroundSnapTests`，注入 BoxCollider 板当"地形根"——**不为测试建地形**）：

1. `Npc_View_Is_Snapped_Onto_The_Terrain_Surface` —— 服务端 y=0 → 视图 y 变成地形面；
2. `Player_And_Bot_Views_Keep_Their_Server_Y` —— 真人/bot 视图**不**吸附；
3. `Canopy_Above_The_Terrain_Does_Not_Win` —— 头顶 12m 有遮挡体时仍取地形（**不加身份校验就会吸到树冠**，回归线）；
4. `Miss_Keeps_Server_Y_Instead_Of_Zero` —— 没有地形时保持服务端 y=0.4（**回退 0 会静默埋地**）；
5. `Samples_Are_Reused_Until_The_Npc_Moves_Past_The_Radius` —— 原地复用、挪出半径重采。

## 5. 实机探针（Play 模式，只读，验收）

流程：`editor-application-set-state {"isPlaying":true}` → 等 20s（连服 + 实体就位）→ 连跑 3 轮探针（间隔 2s）→ 退出 Play。
探针逐只打印：**视图根世界 y**、**同一 XZ 向下射线的"只认地形"地面 y**、差值，外加裸射线的首个命中（负对照）。
地面真值用"命中体 GameObject == LowpolyTerrain 或其子物体"独立判定，**不读实现里的缓存**。

| 轮次 | id | 实体 XZ | 视图 y | 地面 y | \|Δ\| | 裸射线首个命中（负对照） |
|---|---|---|---|---|---|---|
| 1 | 2000001 | (−24.85, 4.81) | 0.278 | 0.278 | **0.0000** | NpcBody y=1.535 |
| 1 | 2000002 | (0.48, −15.88) | −0.241 | −0.241 | **0.0000** | SlimePBR_2000002_T1 y=1.199 |
| 1 | 2000003 | (3.21, −36.75) | −2.476 | −2.472 | **0.0035** | SlimePBR_2000003_T1 y=−1.042 |
| 1 | 2000004 | (−0.88, −15.48) | −0.005 | −0.005 | **0.0000** | NpcBody y=1.250 |
| 2 | 2000001 | (−33.71, 6.29) | 0.367 | 0.367 | **0.0001** | NpcBody y=1.621 |
| 2 | 2000002 | (0.48, −15.88) | −0.241 | −0.241 | **0.0000** | SlimePBR_2000002_T1 y=1.199 |
| 2 | 2000003 | (1.58, −31.61) | −0.867 | −0.862 | **0.0045** | SlimePBR_2000003_T1 y=0.558 |
| 2 | 2000004 | (−0.88, −15.48) | −0.005 | −0.005 | **0.0000** | NpcBody y=1.250 |
| 3 | 2000001 | (−39.59, −0.41) | 0.081 | 0.081 | **0.0003** | NpcBody y=1.336 |
| 3 | 2000002 | (0.48, −15.88) | −0.241 | −0.241 | **0.0000** | SlimePBR_2000002_T1 y=1.199 |
| 3 | 2000003 | (−2.26, −32.32) | 0.000 | 0.000 | **0.0000** | SlimePBR_2000003_T1 y=1.440 |
| 3 | 2000004 | (−0.88, −15.48) | −0.005 | −0.005 | **0.0000** | NpcBody y=1.250 |

**结论：12/12 读数 ≤ 0.0045 ≪ 0.01 ✓**（2000001/2000003 是走动的怪，2000002/2000004 是静止的；
走动时的 0.0035/0.0045 就是 1cm 复用半径 × 49% 堤坡的残差，与设计式吻合）。探针日志里的 `maxDelta` 三轮分别
0.0035 / 0.0045 / 0.0003。同一次 Play 会话里**没有** `[Sync] no ground` 警告（地形在场，采样都命中）。

**负对照（证明"掩码/身份"不是装饰）**——两处已知树点，裸射线的首个命中与地形真值：

```
[T] xz=(-25,-15) naiveFirst=Tree2 y=6.004 | terrainOnly y=0.309
[T] xz=(20,-20)  naiveFirst=Tree1 y=3.149 | terrainOnly y=0.082
```

裸射线会把怪吸到 6.0m / 3.1m 的树冠上（与任务给的先行探针数值一致）；探针里各只怪的"首个命中"也多是
**怪自己的模型/身体碰撞体**（NpcBody / SlimePBR，1.2~1.6m）——又一次说明不校验身份就只能是错的。

## 6. 疑虑 / 边界

1. **地形根按对象名解析**（`GameObject.Find("LowpolyTerrain")`，常量 `TerrainObjectName`）：改名/换地形资产 ⇒ 采样
   全部未命中 ⇒ 静默回退服务端 y（怪又埋回去，但不炸、不埋更深，且有一条一次性 warning）。若将来给地形
   单独开一层，把 `GroundMask` 指过去即可（快路径就永远是它，身份校验保留作兜底）。
2. **复用的残差是坡度的函数**：本场景实测坡 ~49% ⇒ ≤7mm；换更陡的地形要相应收小 `GroundSampleRadius`
   （或者改成"格内插值"，这次没做——为 1 件事加了复杂度）。
3. 探针是**瞬时**采样（12 个读数）；走动中的怪在两次读数之间不保证每个中间帧 ≤1cm，但设计上界（7mm @49% 坡）
   覆盖全过程。
4. 服务端 y 仍是 0，快照里那份 y 依然没有意义——**没有**顺手去改协议/服务端（本次范围外）。

## 7. 命令清单（可复现）

```bash
# 编辑态
unity-mcp-cli run-tool assets-refresh --path "E:/CodeForJob/Cool" --input '{"options":"ForceSynchronousImport"}'
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode"}' --timeout 600000

# 实机（Play）：进/出 Play
unity-mcp-cli run-tool editor-application-set-state --path "E:/CodeForJob/Cool" --input '{"isPlaying":true}'
unity-mcp-cli run-tool script-execute --path "E:/CodeForJob/Cool" --input "$(cat production/verify/_npc_ground_play_probe.json)"
unity-mcp-cli run-tool console-get-logs --path "E:/CodeForJob/Cool" --input '{"logTypeFilter":"Log","maxEntries":60}'
unity-mcp-cli run-tool editor-application-set-state --path "E:/CodeForJob/Cool" --input '{"isPlaying":false}'
```

探针语料（只读，未改任何场景/资产）：`production/verify/_npc_ground_play_probe.json`（验收读数）、
`_npc_ground_play_probe2.json`（缓存陈旧度与局部坡度诊断）、`_npc_ground_play_probe3.json`（树冠负对照）、
`_npc_ground_layer_probe.json`（层/相机/光照掩码核查）。

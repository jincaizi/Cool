# 本地玩家受击与状态表现增强设计

**Date:** 2026-10-22
**Engine:** Unity 2022.3.25f1
**Layer:** `AOT/Protocol`、`Hotfix/Sys3C`、`Hotfix/SyncLayer`、`Hotfix/Nameplate`、`Hotfix/VFX`、`Server/Handler`
**状态:** 待实现

---

## 0. 结论摘要

为**本地玩家**增加四类表现：

1. HP < 20% 时身体持续泛红，治疗回到阈值以上自动清除。
2. 受击时身体泛红约 1s（可配置）；泛红持续期间再次受击**不重触发**，结束后才可再次触发。
3. 被**玩家**（含 bot）攻击时触发红色屏幕闪屏；被**怪物**攻击不触发。
4. 实际回血时在角色身上播放绿色粒子。

实现分三层：协议/事件提供“伤害来源”与“治疗发生”的事实；玩家表现组件负责身体染色与粒子；UI 层负责屏幕闪屏。

### 已拍板的决策

| 决策 | 选定 | 理由 |
|---|---|---|
| 作用范围 | **仅本地玩家** | 屏幕闪屏只对本地有意义；远端玩家/怪物在客户端没有完整 HP 同步 |
| 回血逻辑 | **真正实现 `Heal` + 粒子** | 当前玩家 `Heal` 是空实现，只做粒子会永远不触发 |
| 回血触发源 | **仅 Editor/Development 的调试键 H** | 现有治疗链路打不到玩家（见 §4.3.1）；调试键用于实机验收，不进入正式包 |
| 参数位置 | **GameSettings** | 现有 `HitFlash*` 已在同一资产，集中可调 |
| 泛红实现 | **MaterialPropertyBlock 改 `_Color`** | 玩家全部 SkinnedMesh 共用 `PBR_Default`（Standard），零额外资源 |
| 泛红叠加 | **低血量层 + 受击层** | 低血量持续、受击更亮；受击层衰减回落到底层 |
| 受击重触发 | **泛红期间忽略新受击** | 用户明确要求 |
| 屏幕闪屏 | **保留现有 3 连闪，冷却改为可配短值** | 用户选择；只修初始冷却与颜色 |
| 玩家来源判定 | **协议新增 `SourceKind`** | 不靠 id 段猜服务端实现 |
| bot 攻击 | **按玩家处理** | bot 与玩家同源，闪屏语义一致 |
| 格挡成功 | **不泛红、不闪屏** | 格挡已有 `DefendHit` 独立表现 |
| 死亡 | **不泛红** | 现有受击事件在 HP≤0 时不发，保持 |

---

## 1. 现状与证据

| # | 事实 | 出处 |
|---|---|---|
| 1 | 玩家 `Heal` 是空方法：`public void Heal(float amount) { }` | `Sys3C/Sys3CEntry.cs:935` |
| 2 | `HealEffectData.Apply` 会调 `target.Heal(healAmount)`，因此治疗对玩家完全无效 | `Skills/Effect/EffectData.cs:214-228` |
| 3 | 屏幕闪屏类已存在但**全项目无调用者**；`EntityDisplayManager` 只创建不接线 | `Nameplate/DamageScreenEffect.cs:39`；`Nameplate/EntityDisplayManager.cs:44` |
| 4 | 闪屏资源参数：`HitFlashDuration=1`、`HitFlashCD=20`、颜色橙红 | `Resources/Setting/GameSettings.asset:17-20`；`AOT/DataDefinition/GameSettings.cs:27-38` |
| 5 | 玩家受击事件没有“来源”字段，只有 EntityId/伤害/部位/方向/元素色 | `Sys3C/Core/Events/PlayerTakeDamageEvent.cs:10-29` |
| 6 | 网络伤害消息有 `SourcePlayerId`，但本地回调丢掉了它，只传 target/damage/crit/dir/hp/blocked | `AOT/Protocol/Messages/Combat.cs:85-101`；`SyncLayer/CombatNetworkBridge.cs:112-113` |
| 7 | 服务端明确知道施法者是不是 NPC（`resolution.IsNpcCaster`），并把 `SourcePlayerId=CasterPlayerId` 下发 | `Server/Handler/CastHandler.cs:105,424-437` |
| 8 | 怪物攻击也走 `CastRequest`，因此网络模式下玩家挨打同样经过 `DamageResult` | `Server/Monster/MonsterBtActions.cs:91-101` |
| 9 | 怪物 NPC id 从 `2_000_000` 起；bot id 段为 `1_000_000+` | `Server/Monster/MonsterModule.cs:21`；`Server/Aoi/BotRunner.cs:90` |
| 10 | `EntityKind`：Player=0 / Npc=1 / PlayerBot=2 | `AOT/Protocol/Messages/WorldSnapshot.cs:37-42` |
| 11 | 男/女玩家预制体各 4 个 `SkinnedMeshRenderer` 全部引用同一材质 `PBR_Default`（guid `f323cced...`） | `Assets/Prefabs/Character/FemalePlayer.prefab:630-638`；`Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab` 的 4 个 SMR |
| 12 | `PBR_Default` 使用内置 Standard shader（fileID 46），支持 `_Color` | `Assets/RpgDuo/Material/PBR_Default.mat:11` |
| 13 | 同一个玩家预制体被远端视图复用：`_playerAvatarPrefab = MaleCharacterPBR`，`RemoteEntityRegistry` 会实例化它 | `SyncLayer/ClientNetworkHost.cs:59`；`SyncLayer/RemoteEntityRegistry.cs:307` |
| 14 | 本地玩家由 `PlayerSpawner` 在选角后运行时实例化 | `Sys3C/Character/PlayerSpawner.cs:43` |
| 15 | EventBus 有全局 `Emit` 与精确投递 `TargetedEmit`；玩家受击走全局 Emit，怪物单机受击走 TargetedEmit | `Skills/EventBus.cs:75,159`；`Sys3CEntry.cs:628,714`；`Monster/MonsterEntity.cs:230` |
| 16 | 怪物受击闪红组件 `HitFlashVFX` 只订阅 targeted `MonsterTakeDamageEvent`，且静态开关默认关闭 | `VFX/HitFlashVFX.cs:11,31` |
| 17 | 回血飘字配置存在（`FloatText_Heal.asset`、`FloatTextType.Heal`），但没有任何 Heal 事件/触发链路 | `Nameplate/FloatTextRenderer.cs:100` |
| 18 | HUD HP 由 `Sys3CEntry.Update` 脏检查推动，所有写 `_currentHP` 的路径自动同步 | `Sys3C/Core/Combat/Sys3CEntryStatsProvider.cs:12-13`；`Sys3CEntry.cs:230-236` |
| 19 | `Sys3CEntry` 只实现 `IDamageable`，**没有实现 `IEffectTarget`** | `Sys3CEntry.cs:25` |
| 20 | 玩家侧唯一的 `IEffectTarget` 是 `SkillOwnerProxy`，其 `Heal` 为空、`Stats` 返回 null | `Sys3CEntry.cs:923-938` |
| 21 | `SkillExecutor` 只对物理查询命中的 `IEffectTarget` 应用效果，并明确跳过施法者自身后代 | `Skills/Runtime/SkillExecutor.cs:596-600,640,669,683` |
| 22 | `HealEffectData.IsTickEffect = true`，但 `OnTick`/`IsTickEffect` 全项目无调用者；也没有治疗技能资产 | `Skills/Effect/EffectData.cs:87-101,209` |

---

## 2. 目标与非目标

### 目标

1. HP < 20%（可配）时本地玩家身体持续泛红；治疗回到阈值以上自动清除。
2. 受击时身体泛红约 1s（可配）；泛红期间再次受击不重触发；结束后可再次触发。
3. 被玩家（含 bot）攻击时屏幕红色闪屏；被怪物攻击不触发。
4. 实际回血时在角色身上播放绿色粒子；回血本身要真正增加 HP。
5. 所有表现参数集中在 `GameSettings`，不新增预制体序列化配置。
6. 不改远端玩家/怪物表现，不改现有格挡/死亡表现。

### 非目标

- 远端玩家、怪物的泛红/回血粒子。
- 格挡成功的泛红或闪屏。
- 回血飘字、低血量屏幕边缘 vignette、受击音效。
- 单机模式下“被玩家攻击”（单机不存在其他玩家）。
- 泛红/闪屏的对象池化（本地玩家只有一个，实例化/置空即可）。

---

## 3. 架构与数据流

```
玩家来源伤害：
Server CastHandler ──DamageResult(SourceKind)──> CombatNetworkBridge
  ──ApplyServerDamage(..., sourceIsPlayer)──> Sys3CEntry
     ├─ 非格挡且存活 ──Emit──> PlayerTakeDamageEvent ──> PlayerBodyTint（受击泛红）
     └─ 玩家来源 ───────Emit──> LocalPlayerHitByPlayerEvent ──> EntityDisplayManager（屏幕闪屏）

治疗：
HealEffectData.Apply ──> Sys3CEntry.Heal(amount)
  └─ 实际回血 ──Emit──> PlayerHealedEvent ──> PlayerHealVFX（绿色粒子）

低血量：
PlayerBodyTint 每帧读 Sys3CEntry.CurrentHp / MaxHp（无需事件）
```

---

## 4. 详细设计

### 4.1 协议与服务端

`DamageResult` 新增字段（只加不删，追加高位 tag）：

```csharp
[ProtoMember(8)] public int SourceKind { get; set; } // 值同 EntityKind：0 Player / 1 Npc / 2 PlayerBot
```

`Server/Handler/CastHandler.BroadcastDamageAsync`：

```csharp
SourceKind = resolution.IsNpcCaster ? (int)EntityKind.Npc : (int)EntityKind.Player,
```

bot 施法时 `IsNpcCaster == false`，自然归类为 Player，符合“bot 按玩家处理”。

### 4.2 客户端伤害链路

`CombatNetworkBridge` 的本地受伤回调增加一个参数 `bool sourceIsPlayer`：

```csharp
bool sourceIsPlayer = result.SourceKind != (int)EntityKind.Npc;
_onDamageToLocalPlayer?.Invoke(hit.TargetPlayerId, hit.Damage, hit.IsCritical,
    Vector3.zero, hit.TargetRemainingHp, hit.IsBlocked, sourceIsPlayer);
```

`ClientNetworkHost` 的 lambda 原样透传；`Sys3CEntry.ApplyServerDamage` 增加可选参数：

```csharp
public void ApplyServerDamage(float damage, Vector3 hitDirection, float? serverRemainingHp = null,
    bool isBlocked = false, bool sourceIsPlayer = false)
```

在非格挡、非致死分支（已有 `PlayerTakeDamageEvent` 的位置）之后追加：

```csharp
if (sourceIsPlayer && _currentHP > 0)
    EventBus.Emit(new LocalPlayerHitByPlayerEvent(gameObject.GetInstanceID()));
```

格挡分支在到达这里之前已 `return`，因此不闪屏。

### 4.3 治疗

`Sys3CEntry.Heal`：

```csharp
public void Heal(float amount)
{
    if (amount <= 0f || _currentHP <= 0f) return;          // 死亡不可治疗
    float before = _currentHP;
    _currentHP = Mathf.Min(_maxHP, _currentHP + amount);
    float actual = _currentHP - before;
    if (actual > 0f)
        EventBus.Emit(new PlayerHealedEvent(gameObject.GetInstanceID(), actual));
}
```

- HUD 由现有脏检查下一帧刷新，不额外插桩。
- `SkillExecutor` 对非 `IDamageable` 目标调用 `Heal(-damage)` 的负数路径被 `amount <= 0` 忽略，语义不变。

#### 4.3.1 治疗可达性与调试触发

现状（详见 §1 第 19-21 行）：玩家不实现 `IEffectTarget`；`SkillOwnerProxy.Heal` 为空；`SkillExecutor` 又排除施法者自身，因此**没有生产治疗源能调用到玩家 `Heal`**。

本次不改造技能目标筛选，只提供最小可验收触发：

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Sys3CEntry.Update 内，放在输入处理附近
if (UnityEngine.Input.GetKeyDown(KeyCode.H))
    Heal(30f);
#endif
```

- 只在 Editor / Development Build 编译，正式包无此按键。
- 不依赖 `InputManager` 的移动/技能输入，避免污染移动端适配。
- 后续接入真正的治疗技能/消耗品时，直接调用 `Sys3CEntry.Heal` 即可，无需改表现层。

### 4.4 新增事件

`Sys3C/Core/Events/PlayerFeedbackEvents.cs`：

```csharp
public struct PlayerHealedEvent : IEvent
{
    public int EntityId;
    public float Amount;
}

public struct LocalPlayerHitByPlayerEvent : IEvent
{
    public int EntityId; // 本地玩家 GameObject 实例 id（当前只用于日志/扩展）
}
```

### 4.5 `HitTintState`（纯逻辑，可单测）

职责：受击泛红的触发门与计时。

```csharp
public sealed class HitTintState
{
    private float _remaining;

    public bool IsHitActive => _remaining > 0f;

    /// 返回 false 表示“泛红中，忽略这次受击”
    public bool TryTrigger(float duration)
    {
        if (_remaining > 0f || duration <= 0f) return false;
        _remaining = duration;
        return true;
    }

    public void Tick(float deltaTime)
        => _remaining = Mathf.Max(0f, _remaining - deltaTime);

    public float Amount(float duration)
        => duration > 0f ? Mathf.Clamp01(_remaining / duration) : 0f;
}
```

### 4.6 `PlayerBodyTint`（MonoBehaviour）

由 `PlayerSpawner` 在实例化玩家后 `AddComponent<PlayerBodyTint>()`，**不挂预制体**——同一预制体被远端视图复用，挂上去会污染远端。

- `Awake`：`GetComponent<Sys3CEntry>()`；`GetComponentsInChildren<SkinnedMeshRenderer>(true)` 缓存；记录每个 renderer 的 `sharedMaterial.GetColor("_Color")` 原值（仅材质 `HasProperty("_Color")` 的 renderer 参与，避免缺属性材质报错）。只染 SkinnedMesh（身体/头/发/披风），不染武器、眼睛等 MeshRenderer。
- `OnEnable`：`EventBus.Subscribe<PlayerTakeDamageEvent>(OnDamaged)`。
- `OnDamaged`：`if (e.EntityId != gameObject.GetInstanceID()) return;` → `_state.TryTrigger(settings.HitTintDuration)`。
- `Update`：
  - `_state.Tick(dt)`；
  - `hpPercent = entry.MaxHp > 0 ? entry.CurrentHp / entry.MaxHp : 1f`；
  - `lowActive = !entry.IsDead && hpPercent > 0f && hpPercent < settings.LowHpThreshold`（HP=0 或尚未初始化时不亮）；
  - 组合：`c = Lerp(original, lowColor.rgb, lowColor.a * (lowActive ? 1 : 0))`，再 `c = Lerp(c, hitColor.rgb, hitColor.a * _state.Amount(duration))`；
  - 用 `MaterialPropertyBlock` 写 `_Color`；所有层都为 0 时 `SetPropertyBlock(null)` 清除。
- `OnDisable/OnDestroy`：清 property block、取消订阅、清理状态。
- 死亡/复活：`IsDead` 抑制低血量层；HP 回满后自动清除，无需额外逻辑。

### 4.7 `PlayerHealVFX`（MonoBehaviour）

同样由 `PlayerSpawner` 运行时挂载。

- `OnEnable` 订阅 `PlayerHealedEvent`，按 `EntityId == gameObject.GetInstanceID()` 过滤。
- 收到事件：`Instantiate(settings.HealVfxPrefab, transform.position + Vector3.up * 1.0f, Quaternion.identity, transform)`；`GameSettings.Instance == null` 或 `prefab == null` 时静默返回。
- 自动销毁：读取粒子 `main.duration + startLifetime` 的最大值再加 0.5s 作为生命周期；没有 ParticleSystem 时用 2s 兜底。
- 粒子 `simulationSpace = Local`，随角色移动（回血效果应贴着身体，而不是留在原地）。

### 4.8 绿色回血粒子预制体

新建 `Assets/Prefabs/VFX/HealGreenParticles.prefab`：

- 非循环，duration ≈ 0.8s；
- 绿色 `startColor`，球半径 0.4 的区域向上飘；
- burst ≈ 25，startSpeed ≈ 0.6，gravityModifier ≈ -0.05；
- 材质使用内置 Default-Particle（与 `HitSpark.prefab` 的 `fileID: 10301` 相同），不新增材质资产。

### 4.9 屏幕闪屏

- `EntityDisplayManager.OnEnable/OnDisable` 订阅/退订 `LocalPlayerHitByPlayerEvent`，回调调用 `_damageScreenEffect.Flash()`。
- `DamageScreenEffect`：
  - 颜色改读 `GameSettings.PlayerHitFlashColor`；
  - 冷却改读 `GameSettings.PlayerHitFlashCooldown`；
  - 构造函数把 `_lastFlashTime = -_cooldown`，修复 `Time.time < cooldown` 时开局不闪的边界；
  - 保留现有 3 连闪 + 1.5s 淡出序列与 `FlashAlpha = 0.15`。
- 闪屏只在玩家来源、非格挡、非致死时触发。

### 4.10 GameSettings 新增配置

```csharp
[Header("Player Feedback")]
[Range(0f, 1f)] public float LowHpThreshold = 0.2f;
public Color LowHpTintColor = new Color(0.75f, 0.05f, 0.05f, 0.45f); // alpha = 混合强度
public Color HitTintColor = new Color(1f, 0.1f, 0.1f, 0.7f);          // alpha = 混合强度
public float HitTintDuration = 1f;
public Color PlayerHitFlashColor = new Color(0.9f, 0.05f, 0.05f, 1f);
public float PlayerHitFlashCooldown = 0.8f;
public GameObject HealVfxPrefab;
```

现有 `HitFlash*` 字段保持原语义（怪物受击闪红用），不改。

> 实现时必须把上述默认值**显式写进** `Assets/Resources/Setting/GameSettings.asset`。新增字段在旧资产里通常能用字段初始化器兜底，但显式写入可避免 Inspector 显示为 0/null（尤其 `HealVfxPrefab` 是对象引用）。

---

## 5. 测试计划

### EditMode

1. `HitTintState`：
   - 首次触发成功；持续期间再次触发返回 false；
   - `Tick` 超过 duration 后可再次触发；
   - `Amount` 随时间线性下降。
2. 协议：`DamageResult.SourceKind` protobuf 往返；按现有 `ProtocolShapeGuardTests` 的惯例新增 `SourceKind` 必须挂在 **tag 8** 的 pin 测试。
3. 服务端：NPC 施法 `SourceKind == Npc`；玩家/bot 施法 `SourceKind == Player`。
4. `CombatNetworkBridge`：`SourceKind=Npc` → 本地回调 `sourceIsPlayer=false`；Player/PlayerBot → true。

### PlayMode

1. `Heal`：正数加血并钳制 MaxHp；死亡时忽略；`PlayerHealedEvent` 只在实际回血时发；HUD `OnHPChanged` 收到更新。
2. 调试键：Editor/Development 下按 `H` → HP +30 且绿色粒子出现；用条件编译断言正式包不含该分支（代码审查即可）。
3. 受击泛红：实例化玩家 + 手动挂 `PlayerBodyTint`；发 `PlayerTakeDamageEvent` 后 MPB `_Color` 偏离原值；1s 内第二次受击不改变计时；结束后可再次触发。
4. 低血量：把 HP 打到 20% 以下，MPB 持续偏红；治疗到阈值以上后恢复原色。
5. 闪屏：玩家来源伤害后 overlay `color.a > 0`；NPC 来源伤害后保持 0。
6. 回血粒子：实际回血后玩家子物体下出现 `HealGreenParticles(Clone)`；无效治疗不产生。

### 视觉验收

MCP 截图对比三态：低血量持续泛红、受击瞬间泛红、回血绿色粒子。断言数值（MPB 颜色值、overlay alpha、HP）优先于肉眼判断。

---

## 6. 风险与已知限制

1. **乘性染色偏暗**：Standard `_Color` 是 albedo 乘数，深红会压暗纹理。若验收觉得不够醒目，后续加 `_EmissionColor` 作为第二层（需改材质 keyword）。
2. **协议默认值**：新增 `SourceKind` 默认 0=Player；旧服务端不填时，怪物伤害会被旧客户端当成玩家来源。由于客户端闪屏本次才接线，实际风险为 0；但相关测试必须显式设置该字段。
3. **预制体复用**：玩家预制体同时被远端视图实例化，因此组件必须运行时挂载，不能挂预制体。若未来改为预制体挂载，必须加“仅本地玩家”守卫。
4. **格挡/死亡边界**：格挡不泛红不闪屏；死亡瞬间不泛红。这是有意行为，测试需固化。
5. **低血量轮询**：每帧读两个 float，开销可忽略；不做事件是为了覆盖服务端伤害、治疗、复活三个写入源。
6. **回血粒子资产**：新增 prefab 需要创建并纳入版本控制；若美术后续替换，只需改 GameSettings 引用。
7. **正式包没有回血触发源**：调试键只在 Editor/Development 编译；正式包内的回血表现要等后续治疗技能/消耗品调用 `Sys3CEntry.Heal` 才会出现。这是本次有意的范围边界。

---

## 7. 实施顺序（供 writing-plans 参考）

1. 协议 + 服务端 `SourceKind` + 客户端桥接 + 相关测试。
2. `Sys3CEntry.Heal` + 两个新事件 + Editor/Development 调试键 H + 测试。
3. `HitTintState` + `PlayerBodyTint` + GameSettings 字段 + PlayerSpawner 挂载。
4. `HealGreenParticles.prefab` + `PlayerHealVFX` + PlayerSpawner 挂载。
5. `DamageScreenEffect` 参数化 + `EntityDisplayManager` 接线。
6. PlayMode 测试 + 截图验收。

---

## 8. 验收标准

- [ ] HP < 20% 时身体持续泛红；治疗回 20% 以上后颜色恢复。
- [ ] 受击泛红约 1s；期间连续受击不重新计时；结束后可再次触发。
- [ ] 玩家/bot 来源伤害触发红色闪屏；怪物来源不触发。
- [ ] 格挡与死亡不触发泛红/闪屏。
- [ ] 实际回血时出现绿色粒子；无效治疗/死亡状态不出现。
- [ ] Editor/Development 下按 `H` 能回 30 HP 并看到绿色粒子；正式包不含该调试分支。
- [ ] EditMode/PlayMode 测试全绿，数值断言覆盖 MPB 颜色、overlay alpha、HP 变化。

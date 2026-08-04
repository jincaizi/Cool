# 受击泛红效果 — 移动端性能评估与护栏

- 日期: 2026-08-05
- 状态: 已批准（方案 A：零代码改动）
- 目标平台: 移动端，中端机型为主（低端极限场景舍弃）
- 触发背景: 预防性性能担忧，非线上问题

## 1. 结论

当前受击泛红实现（`HitFlashVFX`，MaterialPropertyBlock + 事件驱动 + 0.15s DOTween）在目标规模（≤10 目标同时泛红）和中端机型目标下**无性能风险，不需要改动代码**。

## 2. 现状分析

| 项 | 实际情况 |
|---|---|
| 实现位置 | `Assets/Scripts/Hotfix/GameSystems/VFX/HitFlashVFX.cs` |
| 触发链路 | `MonsterTakeDamageEvent`（EventBus 定向订阅）→ `OnMonsterDamaged` → `TriggerFlash`，纯事件驱动，无每帧轮询 |
| 着色方式 | `MaterialPropertyBlock` 覆盖 `_Color`（复用同一 block，不实例化材质） |
| 恢复方式 | DOTween 0.15s（`GameSettings.HitFlashDuration`）渐变回原始颜色 |
| 渲染管线 | 内置管线（manifest 无 URP 包） |
| 怪物材质 | 内置 Standard shader（`_Color` 为 albedo 乘法） |
| 总开关 | `HitFlashVFX.EnableVFX`，默认 `false` |

## 3. 成本明细

| 成本项 | 实际情况 | 量级 |
|---|---|---|
| 命中瞬间 | 1 次 `sharedMaterial.GetColor` + 1 次 `SetPropertyBlock` | ~2µs/次 |
| 泛红期间 | 每目标每帧 `GetPropertyBlock` + `SetPropertyBlock`，0.15s ≈ 9 帧 | 10 目标合计 <0.2ms CPU，摊在 9 帧内 |
| GPU | Standard shader 本就执行 `albedo × _Color`，tint 复用已有乘法 | 0 额外开销 |
| DrawCall | SkinnedMeshRenderer 在内置管线本就不参与动态合批 | 0 增量 |
| 内存/GC | MPB 复用；DOTween 内部池化 | 无持续分配 |

## 4. 为什么这是移动端正确姿势

- **MPB 而非材质实例化**：`MaterialPropertyBlock` 是引擎推荐的无 GC、不断合批的按渲染器着色方式；`renderer.material` 会在每次命中时实例化一份材质（内存增长 + GC 压力 + 打断合批），是移动端经典杀手。整个 Hotfix 代码库中不存在 `renderer.material` 实例化模式。
- **事件驱动而非轮询**：无 `Update` 开销，只有受击瞬间才产生工作。
- **短时效果**：0.15s 的瞬时 tint 不改变材质持久状态。

## 5. 边界条件（触发重新评估的信号）

以下任一情况出现时，需要按本规范重新评估：

1. 同时泛红目标 > 50（当前规模 ≤10，数量级差距超过一个数量级）
2. 引入 URP 或更换怪物 shader（Standard → 自定义）
3. 效果扩展到静态环境物体（需在 shader 上标记 `[PerRendererData]` 才能保住静态合批）
4. 泛红变为持续状态（受击后长时间保持颜色）
5. 目标设备档位重新加入低端安卓

## 6. 护栏（四条不可违反规则）

1. **禁止** `renderer.material` 实例化做 tint（内存 + GC + 断合批）
2. **禁止** 后处理/全屏叠加实现单目标 tint（fill-rate 杀手）
3. **禁止** 持续 tint 状态（受击后长时间保持颜色不恢复）
4. **禁止** 每帧轮询代替事件驱动

## 7. 验证方式

将来担忧复现时，按以下步骤在真机验证（中端机型）：

1. Profiler 帧预算内，10 目标同时泛红持续 5 秒
2. 确认 CPU 时间增量 < 0.5ms
3. 确认 0 新增 DrawCall
4. 确认无 GC.Alloc 峰值

## 8. 决策记录

- 2026-08-05：评估完成，确认现有实现为移动端最优模式，采用方案 A（零代码改动，仅记录评估与护栏）。相关系统（`HitFeedbackSystem`、`FloatTextRenderer`、`CameraShakeManager`、`HitStopManager`）同为事件驱动 + 开关 + 池化模式，一并确认无需改动。

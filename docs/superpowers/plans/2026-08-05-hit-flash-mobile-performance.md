# 受击泛红效果移动端性能评估 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 确认受击泛红效果（HitFlashVFX）在移动端目标规模下无需代码改动，仅完成验收与文档留档。

**Architecture:** 本计划对应 spec `docs/superpowers/specs/2026-08-05-hit-flash-mobile-performance-design.md` 的方案 A（零代码改动）。没有实现任务；唯一可执行的动作是验收确认（spec 已提交、代码事实与 spec 证据一致）和一份留给将来的真机验证清单。

**Tech Stack:** Unity 2022.3 (built-in pipeline), MaterialPropertyBlock, DOTween

**前置条件:** spec 已提交（commit `94344db`）。

---

### Task 1: 验收确认（零改动范围验证）

**Files:**
- Verify: `docs/superpowers/specs/2026-08-05-hit-flash-mobile-performance-design.md`（已提交）
- Verify: `Assets/Scripts/Hotfix/GameSystems/VFX/HitFlashVFX.cs`（只读核对，不修改）

- [ ] **Step 1: 确认 spec 已提交**

Run: `git log --oneline -1 -- docs/superpowers/specs/2026-08-05-hit-flash-mobile-performance-design.md`
Expected: 输出 `94344db docs: hit-flash (mobile damage tint) performance assessment — no code change needed`（或该文件的最新提交）

- [ ] **Step 2: 核对代码事实与 spec 证据一致（只读，禁止修改）**

核对 `Assets/Scripts/Hotfix/GameSystems/VFX/HitFlashVFX.cs`：
1. 第 16-17 行：使用 `MaterialPropertyBlock` 而非 `renderer.material`（spec 第 4 节）
2. 第 31 行：事件驱动订阅 `MonsterTakeDamageEvent`，无 `Update` 轮询（spec 第 2 节）
3. 第 67 行：`flashDuration` 来自 `GameSettings.HitFlashDuration` = 0.15（spec 第 2 节）
4. 第 11 行：`EnableVFX` 默认 `false`（spec 第 2 节总开关）

Run: `git diff HEAD --stat`
Expected: 工作区**不包含** `HitFlashVFX.cs` 的改动（零代码改动原则）

- [ ] **Step 3: 确认无材质实例化模式蔓延**

Run: `grep -rn "\.material\s*=" Assets/Scripts/Hotfix`
Expected: 仅 `WeaponTrailRenderer.cs:49` 的 `_trail.material = _trailMaterial`（共享材质引用赋值，非实例化），无其他命中

- [ ] **Step 4: 收尾记录（无代码提交）**

本任务零代码改动，无需 git 提交。在会话日志记录验收结果即可。

---

### Task 2: 真机验证清单（可选，仅当性能担忧复现时执行）

**Files:**
- None（参考 `docs/superpowers/specs/2026-08-05-hit-flash-mobile-performance-design.md` 第 7 节）

- [ ] **Step 1: 部署到中端真机（非编辑器）**

构建 Android 包安装到中端机型（如骁龙 7 系/天玑 8000 档）。

- [ ] **Step 2: 复现 10 目标同时泛红 5 秒**

开启 `HitFlashVFX.EnableVFX = true`（或由配置开关打开），用 AOE 技能一次命中 ≥10 个怪物。

- [ ] **Step 3: Profiler 采集并核对四项指标**

用 Unity Profiler（或 Android Studio Perfetto）采集战斗片段：
1. CPU 时间增量 < 0.5ms（对比无泛红基线）
2. 0 新增 DrawCall
3. 无 GC.Alloc 峰值
4. 帧率无掉帧（保持 60fps 或目标帧率）

- [ ] **Step 4: 结果记录**

达标 → 关闭清单，结论维持"无需改动"；不达标 → 按 spec 第 5 节边界条件重新评估（此时才考虑方案 B 微型加固或方案 C 统一管理器）。

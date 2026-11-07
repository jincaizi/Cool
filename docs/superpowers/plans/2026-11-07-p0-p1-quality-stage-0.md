# P0/P1 质量改进阶段 0 实施计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 完成阶段 0 的文档与治理基线，使 Gate 0 可以在原 Spec、33 任务计划、三份 ADR、技术偏好、发布范围和测试清单全部可执行后开始。

**架构：** 先建立技术偏好单一来源与三份 ADR，再回写原 Spec 和 33 任务计划，最后生成 `production/quality` 治理文件并用一致性脚本验证阶段 0 退出清单。阶段 0 不改运行时代码；KCP 安全 POC 在独立 `spikes/` 目录执行，不进入生产程序集。

**技术栈：** Markdown、Bash/Git、YAML、JSON、Python 3.12（JSON 校验与测试清单生成）、Unity 2022.3.25f1、.NET 8。

**规格：** [2026-11-07-p0-p1-quality-improvement-execution-design.md](../specs/2026-11-07-p0-p1-quality-improvement-execution-design.md)

---

## 全局约束

- 从当前 `master` 创建阶段 0 分支：`git checkout -b codex/quality/stage-0`。工作树不干净时停止并请求确认，不得丢弃改动。
- 只暂存本任务明确列出的路径；禁止 `git add .` / `git add -A`。
- 阶段 0 不改运行时代码；`spikes/` 下的 POC 不进入 Unity 程序集，也不得被生产代码引用。
- 每个任务结束时运行 `bash tools/check-stage0-consistency.sh`。该脚本在阶段 0 完成前会持续报红，红灯数量必须随任务推进减少；任务 16 必须全绿。
- 所有文档中的构建目标一律使用 Unity 枚举名：`StandaloneWindows64`、`Android`、`iOS`、`WebGL`。禁止 `win64` 等别名。
- 所有新建的 `Assets/` 文件必须同时提交对应 `.meta`；本阶段只新建文档与脚本，不新建 Unity 资源。
- 提交信息使用英文 conventional commit，与仓库现有历史一致。

## 文件结构总览

| 文件 | 职责 |
|---|---|
| `tools/check-stage0-consistency.sh` | 阶段 0 唯一自动验收脚本；检查文件、章节、键名、旧默认值和 POC 证据 |
| `docs/technical-preferences.md` | 技术偏好唯一人工维护源 |
| `.claude/docs/technical-preferences.md` | 由脚本生成的 Claude 入口，禁止手改 |
| `.codex/docs/technical-preferences.md` | 由脚本生成的 Codex 入口，禁止手改 |
| `tools/sync-technical-preferences.sh` | 从唯一源生成两个入口并写入 hash marker |
| `docs/architecture/adr-0001-platform-transport-matrix.md` | 平台传输决策 |
| `docs/architecture/adr-0002-quality-gate-evidence-model.md` | Gate 状态机与证据模型 |
| `docs/architecture/adr-0003-kcp-session-security.md` | 移动 KCP 公网安全 provider 决策 |
| `spikes/kcp-security-poc/` | ADR-0003 的候选评估与最小 POC，不进入生产程序集 |
| `production/quality/release-scope.yaml` | 本轮 blocking 平台、设备/browser lane、批准人 |
| `production/quality/test-manifest.json` | assembly/profile/cohort 与测试全名基线 |
| `production/quality/gate-exceptions.json` | 带 owner/expiry 的门禁例外 |
| `production/qa/device-profiles/android-reference.yaml` | Android 性能参考档位；未配置时 Gate 为 `BLOCKED_NOT_VERIFIED` |
| `tools/generate-test-manifest.py` | 从测试源码生成 manifest |
| `production/quality/technical-preferences-sentinel.md` | 两个工具链 consumer sentinel 证据 |
| `production/quality/stage-0-report.md` | 阶段 0 退出报告与 Git SHA 记录 |
| `docs/superpowers/specs/2026-11-05-p0-p1-quality-improvement-design.md` | 原 Spec，阶段 0 回写平台矩阵、Gate、生命周期契约 |
| `docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md` | 原 33 任务计划，阶段 0 回写复核修订与 Gate 0 设计 |
| `CLAUDE.md` / `AGENTS.md` | 增加技术偏好强制读取入口 |

---

### 任务 1：建立阶段 0 一致性检查器

**文件：**
- 创建：`tools/check-stage0-consistency.sh`

- [ ] **步骤 1：编写检查器**

```bash
#!/usr/bin/env bash
# Stage 0 consistency checker. Exits non-zero until every Stage 0 deliverable
# and revision is present and internally consistent.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

FAIL=0
fail() { echo "FAIL: $*" >&2; FAIL=1; }
pass() { echo "ok: $*"; }
need_file() { if [ -f "$1" ]; then pass "file $1"; else fail "missing file $1"; fi; }
need_grep() { if grep -qF -- "$2" "$1" 2>/dev/null; then pass "$1 contains: $2"; else fail "$1 missing: $2"; fi; }
need_not_grep() { if grep -qF -- "$2" "$1" 2>/dev/null; then fail "$1 still contains: $2"; else pass "$1 excludes: $2"; fi; }

SPEC="docs/superpowers/specs/2026-11-05-p0-p1-quality-improvement-design.md"
DESIGN="docs/superpowers/specs/2026-11-07-p0-p1-quality-improvement-execution-design.md"
PLAN="docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md"
TP="docs/technical-preferences.md"

for f in \
  "$TP" \
  ".claude/docs/technical-preferences.md" \
  ".codex/docs/technical-preferences.md" \
  "tools/sync-technical-preferences.sh" \
  "docs/architecture/adr-0001-platform-transport-matrix.md" \
  "docs/architecture/adr-0002-quality-gate-evidence-model.md" \
  "docs/architecture/adr-0003-kcp-session-security.md" \
  "spikes/kcp-security-poc/RESULT.md" \
  "production/quality/release-scope.yaml" \
  "production/quality/test-manifest.json" \
  "production/quality/gate-exceptions.json" \
  "production/quality/technical-preferences-sentinel.md" \
  "production/quality/stage-0-report.md" \
  "production/quality/stage-0-estimate.md" \
  "production/qa/device-profiles/android-reference.yaml" \
  "tools/generate-test-manifest.py" \
  "$SPEC" "$DESIGN" "$PLAN"; do
  need_file "$f"
done

# Technical preferences source and generated entries.
if [ -f "$TP" ]; then
  need_not_grep "$TP" "[TO BE CONFIGURED]"
  for s in "## Engine & Language" "## Input & Platform" "## Naming Conventions" \
           "## Performance Budgets" "## Testing" "## Forbidden Patterns" \
           "## Allowed Libraries / Addons" "## Architecture Decisions Log"; do
    need_grep "$TP" "$s"
  done
  SRC_HASH="$(sha256sum "$TP" | awk '{print $1}')"
  for g in ".claude/docs/technical-preferences.md" ".codex/docs/technical-preferences.md"; do
    if [ -f "$g" ]; then
      need_grep "$g" "generated, do not edit"
      need_grep "$g" "source-sha256: $SRC_HASH"
    fi
  done
  need_grep "CLAUDE.md" "docs/technical-preferences.md"
  need_grep "AGENTS.md" ".codex/docs/technical-preferences.md"
  need_grep "production/quality/technical-preferences-sentinel.md" "TP-SENTINEL: $SRC_HASH"
fi

# ADR structure.
for adr in \
  "docs/architecture/adr-0001-platform-transport-matrix.md" \
  "docs/architecture/adr-0002-quality-gate-evidence-model.md" \
  "docs/architecture/adr-0003-kcp-session-security.md"; do
  if [ -f "$adr" ]; then
    for h in "## Status" "## Context" "## Decision" "## Consequences" "## Alternatives"; do
      need_grep "$adr" "$h"
    done
  fi
done

# ADR-0003 must record the POC outcome explicitly.
if [ -f "docs/architecture/adr-0003-kcp-session-security.md" ]; then
  need_grep "docs/architecture/adr-0003-kcp-session-security.md" "POC"
  need_grep "docs/architecture/adr-0003-kcp-session-security.md" "fallback"
fi

# Original Spec platform matrix and Gate model.
if [ -f "$SPEC" ]; then
  need_grep "$SPEC" "Android/iOS"
  need_grep "$SPEC" "KCP"
  need_grep "$SPEC" "WebGL"
  need_not_grep "$SPEC" "近期公开测试所有平台默认 WSS"
  need_grep "$SPEC" "PASS_WITH_ADVISORIES"
  need_grep "$SPEC" "BLOCKED_NOT_VERIFIED"
  need_grep "$SPEC" "NOT_IN_SCOPE"
  need_grep "$SPEC" "release-scope.yaml"
fi

# Original plan revisions.
if [ -f "$PLAN" ]; then
  for s in "P1-B1" "P1-B2" "P1-C1" "P1-C2" \
           "GameSys.EditorQuality" "StandaloneWindows64" "TestServerEndpoint" \
           "TransportFactoryBoundaryGuardTests" "KcpPluginPlatformGuardTests" \
           "ClientEnvelope.cs" "DrainResult.cs" "LoginResult.cs" \
           "SceneLease" "VfxCatalog" "HAS_IOS_RUNNER" "release-scope.yaml"; do
    need_grep "$PLAN" "$s"
  done
  L13="$(grep -n '^### 任务 13' "$PLAN" | head -1 | cut -d: -f1)"
  L14="$(grep -n '^### 任务 14' "$PLAN" | head -1 | cut -d: -f1)"
  if [ -n "$L13" ] && [ -n "$L14" ] && [ "$L13" -lt "$L14" ]; then
    pass "task 13 precedes task 14"
  else
    fail "task 13 must precede task 14"
  fi
  if grep -A8 '^### 任务 13' "$PLAN" | grep -qF "P0"; then
    pass "task 13 is P0"
  else
    fail "task 13 priority is not P0"
  fi
fi

# Release scope.
if [ -f "production/quality/release-scope.yaml" ]; then
  for k in milestone blockingPlatforms requiredDeviceLanes requiredBrowserLanes \
           publicEndpoints approver approvedAt sourceGitSha; do
    need_grep "production/quality/release-scope.yaml" "$k:"
  done
fi

# JSON governance files.
if [ -f "production/quality/test-manifest.json" ]; then
  python -m json.tool "production/quality/test-manifest.json" >/dev/null 2>&1 \
    && pass "test-manifest.json is valid JSON" \
    || fail "test-manifest.json is not valid JSON"
  python - <<'PY' || FAIL=1
import json
d = json.load(open("production/quality/test-manifest.json", encoding="utf-8"))
assert "assemblies" in d, "missing assemblies"
assert len(d["assemblies"]) >= 3, "expected at least 3 assemblies"
for name, a in d["assemblies"].items():
    assert a.get("profile"), f"{name}: missing profile"
    assert a.get("cohorts"), f"{name}: missing cohorts"
PY
fi
if [ -f "production/quality/gate-exceptions.json" ]; then
  python -m json.tool "production/quality/gate-exceptions.json" >/dev/null 2>&1 \
    && pass "gate-exceptions.json is valid JSON" \
    || fail "gate-exceptions.json is not valid JSON"
  python - <<'PY' || FAIL=1
import json
d = json.load(open("production/quality/gate-exceptions.json", encoding="utf-8"))
assert isinstance(d.get("exceptions"), list), "exceptions must be a list"
PY
fi

# Android reference profile schema.
if [ -f "production/qa/device-profiles/android-reference.yaml" ]; then
  for k in configured targetFps warmupSeconds sampleSeconds p95Algorithm rawDataFormat; do
    need_grep "production/qa/device-profiles/android-reference.yaml" "$k:"
  done
fi

# Forbidden legacy values and path variants.
if grep -qF "buildTarget win64" "$SPEC" "$PLAN" 2>/dev/null; then
  fail "found legacy -buildTarget win64"
else
  pass "no legacy -buildTarget win64"
fi
if git ls-files | grep -qE '(^|/)\.Codex/'; then
  fail "Git index contains .Codex/ (must be lowercase .codex/)"
else
  pass "no .Codex/ path in Git index"
fi

# Stage 0 activation record must be filled.
if [ -f "$DESIGN" ]; then
  need_not_grep "$DESIGN" "待阶段 0 填写"
fi

if [ "$FAIL" -ne 0 ]; then
  echo "STAGE 0 CHECK: FAIL"
  exit 1
fi
echo "STAGE 0 CHECK: PASS"
```

- [ ] **步骤 2：运行检查器验证红灯**

运行：`chmod +x tools/check-stage0-consistency.sh && bash tools/check-stage0-consistency.sh`

预期：FAIL，输出缺少 `docs/technical-preferences.md`、三份 ADR、`production/quality/*` 等文件。记录输出，作为后续任务的进度基线。

- [ ] **步骤 3：Commit**

```bash
git add tools/check-stage0-consistency.sh
git commit -m "test: add stage 0 consistency checker"
```

---

### 任务 2：建立技术偏好单一来源与同步脚本

**文件：**
- 创建：`docs/technical-preferences.md`
- 创建：`tools/sync-technical-preferences.sh`
- 生成：`.claude/docs/technical-preferences.md`
- 生成：`.codex/docs/technical-preferences.md`
- 创建：`production/quality/technical-preferences-sentinel.md`
- 修改：`CLAUDE.md`、`AGENTS.md`

- [ ] **步骤 1：写唯一源文件**

`docs/technical-preferences.md`：

```markdown
# Technical Preferences

## Engine & Language

- Engine: Unity 2022.3.25f1 LTS
- Language: C# (Unity .NET Standard 2.1 profile); server .NET 8
- Rendering: Built-in Render Pipeline (no URP package in `Packages/manifest.json`)
- Physics: Unity PhysX (client) + server navmesh/ORCA

## Input & Platform

- Target Platforms: Android, iOS, WebGL, Windows (Editor/Standalone tests)
- Input Methods: Touch, Keyboard/Mouse
- Primary Input: Touch (mobile), Keyboard/Mouse (Editor/Standalone)
- Gamepad Support: None
- Touch Support: Full
- Platform Notes: Android/iOS use KCP + Protobuf with the ADR-0003 security provider in public candidates; WebGL uses WebSocket/WSS; one primary transport per platform.

## Naming Conventions

- Classes: PascalCase
- Variables: camelCase; private fields `_camelCase`
- Signals/Events: PascalCase event names; handlers use `On`/`Handle` prefixes as in existing code
- Files: PascalCase `.cs`; asmdef names dotted, e.g. `GameSys.EditorQuality`
- Scenes/Prefabs: PascalCase
- Constants: PascalCase in `*Consts` classes

## Performance Budgets

- Target Framerate: 30 FPS on the Android reference device
- Frame Budget: p95 <= 33.3 ms
- Draw Calls: measure per scene; no fixed budget until the Android reference profile is filled
- Memory Ceiling: no fixed budget until the Android reference profile is filled; steady state must avoid per-frame GC

## Testing

- Framework: NUnit (Unity EditMode/PlayMode), xUnit (server)
- Minimum Coverage: trend baseline; no target assembly may drop more than 1 percentage point
- Required Tests: protocol roundtrip, movement validation, login, bag integrity, resource lifecycle, VFX/monster pool lifecycle

## Forbidden Patterns

- Non-internal MonoBehaviour `GetComponent`/`FindObject*`
- Gameplay `Resources.Load*`
- `AsyncOperationHandle.Task` and `WaitForCompletion()` in player paths
- `async void` connection flows
- Unbounded queues or silent drops of reliable messages
- Client-authoritative movement, defend, or combat state
- Plaintext KCP in non-Development public candidates
- `.Codex/` path variants (only lowercase `.codex/` is valid)

## Allowed Libraries / Addons

- UniTask, Addressables, protobuf-net, KCP (mobile only with ADR-0003 security provider), DOTween, TextMeshPro, NUnit, xUnit
- Security provider: selected by ADR-0003 after POC; no self-rolled cryptographic primitives

## Architecture Decisions Log

- ADR-0001: platform transport matrix
- ADR-0002: quality gate evidence model
- ADR-0003: KCP session security
```

- [ ] **步骤 2：写同步脚本**

`tools/sync-technical-preferences.sh`：

```bash
#!/usr/bin/env bash
# Generate toolchain-specific technical preferences from the single source.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$ROOT/docs/technical-preferences.md"

if [ ! -f "$SRC" ]; then
  echo "missing source: docs/technical-preferences.md" >&2
  exit 1
fi

HASH="$(sha256sum "$SRC" | awk '{print $1}')"

for dst in "$ROOT/.claude/docs/technical-preferences.md" "$ROOT/.codex/docs/technical-preferences.md"; do
  mkdir -p "$(dirname "$dst")"
  {
    echo "<!-- generated, do not edit; source: docs/technical-preferences.md; source-sha256: $HASH -->"
    cat "$SRC"
  } > "$dst"
  echo "wrote $dst"
done
```

- [ ] **步骤 3：生成入口并更新强制读取说明**

运行：

```bash
chmod +x tools/sync-technical-preferences.sh
bash tools/sync-technical-preferences.sh
```

在 `CLAUDE.md` 的 `## 开发约定` 之前新增：

```markdown
## 技术偏好（强制）

- 修改代码前读取 `docs/technical-preferences.md`；Claude 入口使用 `.claude/docs/technical-preferences.md`（generated，禁止手改）。
```

在 `AGENTS.md` 同样位置新增：

```markdown
## 技术偏好（强制）

- 修改代码前读取 `docs/technical-preferences.md`；Codex 入口使用 `.codex/docs/technical-preferences.md`（generated，禁止手改）。
```

- [ ] **步骤 4：写 consumer sentinel 证据**

运行：

```bash
HASH="$(sha256sum docs/technical-preferences.md | awk '{print $1}')"
cat > production/quality/technical-preferences-sentinel.md <<EOF
# Technical Preferences Consumer Sentinel

- Source: \`docs/technical-preferences.md\`
- Marker: \`TP-SENTINEL: $HASH\`
- Claude entry: \`.claude/docs/technical-preferences.md\`
- Codex entry: \`.codex/docs/technical-preferences.md\`
- Claude minimal review: PASS (opened generated file and reported marker)
- Codex minimal review: PASS (opened generated file and reported marker)
- Recorded: 2026-11-07
EOF
```

若 Codex 会话不可用，把对应行改为 `BLOCKED_NOT_VERIFIED` 并在阶段 0 报告中列为未执行；不得写 PASS。

- [ ] **步骤 5：验证**

运行：

```bash
bash tools/sync-technical-preferences.sh
bash tools/check-stage0-consistency.sh
```

预期：技术偏好与 sentinel 相关检查通过；其他文件仍缺失，整体仍 FAIL。

- [ ] **步骤 6：Commit**

```bash
git add docs/technical-preferences.md tools/sync-technical-preferences.sh \
  .claude/docs/technical-preferences.md .codex/docs/technical-preferences.md \
  production/quality/technical-preferences-sentinel.md CLAUDE.md AGENTS.md
git commit -m "docs: add single-source technical preferences"
```

---

### 任务 3：ADR-0001 平台传输矩阵

**文件：**
- 创建：`docs/architecture/adr-0001-platform-transport-matrix.md`

- [ ] **步骤 1：写 ADR**

```markdown
# ADR-0001: Platform Transport Matrix

## Status

Accepted — 2026-11-07.

## Context

The original design treated WebSocket/WSS as the default for all platforms and KCP as an experimental transport. The approved execution design changes this: Android/iOS use KCP + Protobuf as the primary transport, WebGL uses WebSocket/WSS, and Editor/Windows test lanes must select a transport explicitly. `ClientNetworkHost` currently constructs WebSocket directly on PC and WebGL, so production and test paths are not yet aligned.

## Decision

- Android/iOS Player builds use `KcpNetClientAdapter` + Protobuf; public candidates must add the ADR-0003 security provider.
- WebGL Player builds use `WebSocketClientAdapter` with the browser channel; public deployments use `wss://` through a reverse proxy.
- Editor/Windows tests select a transport through the test lane or explicit development configuration; there is no implicit default.
- Exactly one primary transport may be enabled per platform/build configuration.
- Only `NetworkTransportFactory` (or the approved equivalent) may construct concrete transports; the rest of `GameSys` depends on `INetworkClient` and immutable `NetworkEndpoint`.
- KCP plugin/adapter code must be excluded from WebGL builds, and `KcpPluginPlatformGuardTests` / `TransportFactoryBoundaryGuardTests` enforce the boundary.
- Task 13 owns the P0 implementation of the factory, endpoint, plugin exclusion, and guards. Task 30 only maintains the already-passing guards and allowlist.

## Consequences

- P0 must harden the KCP lifecycle and factory boundary before the P0 Gate; Android/iOS real-device security remains a Mobile Release Readiness item.
- WebGL must keep a separate compile/build lane and may not reference KCP types or plugins.
- The original Spec §6.0 WSS-default text must be rewritten; ADR-0001 is the authoritative record of the change.

## Alternatives

- All platforms use WSS: rejected because the approved platform matrix selects KCP for mobile.
- Mobile KCP without a security provider: rejected; public candidates require confidentiality and peer authentication.
- Per-platform asmdef copies of `GameSys`: rejected as a maintenance burden; compile guards plus factory boundary are sufficient.
```

- [ ] **步骤 2：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：ADR-0001 相关检查通过；整体仍 FAIL。

- [ ] **步骤 3：Commit**

```bash
git add docs/architecture/adr-0001-platform-transport-matrix.md
git commit -m "docs: add platform transport matrix ADR"
```

---

### 任务 4：ADR-0002 Gate 证据模型

**文件：**
- 创建：`docs/architecture/adr-0002-quality-gate-evidence-model.md`

- [ ] **步骤 1：写 ADR**

```markdown
# ADR-0002: Quality Gate Evidence Model

## Status

Accepted — 2026-11-07.

## Context

The review requires gates that cannot be green on missing evidence, historical logs, manual server startup, or a dirty worktree. P0/P1 gates now include engineering, mobile release, and WebGL public deployment scopes, so a single PASS/FAIL flag cannot express "not applicable" versus "not verified".

## Decision

- Gate statuses are `PASS`, `PASS_WITH_ADVISORIES`, `FAIL`, `BLOCKED_NOT_VERIFIED`, `NOT_IN_SCOPE`, and `DIAGNOSTIC_ONLY`.
- Every run gets a unique `run-id = <UTC timestamp>-<short-git-sha>`; artifacts live under `production/quality/artifacts/<run-id>/`.
- Evidence must be created after the run starts and must have a SHA-256. Missing, stale, corrupt, or hash-mismatched artifacts fail the gate.
- `--p0`, `--p1`, and release gates require a clean worktree. Dirty runs are `DIAGNOSTIC_ONLY`.
- Test discovery uses `production/quality/test-manifest.json`; unexplained test-count drops and unknown skips/ignored/inconclusive tests fail.
- Exceptions live in `production/quality/gate-exceptions.json` with owner, reason, `expiresAt`, and recovery condition.
- `release-scope.yaml` is the only source of blocking platforms and device/browser lanes; it is copied into the run artifact and cannot change during the run.
- A gate item may be claimed in only one column: Engineering Gate, Mobile Release Readiness, or WebGL Public Deployment Readiness.
- No historical Console logs, manual pre-started servers, or unverified platform state may be used as evidence.

## Consequences

- `quality-gate.ps1` is the authoritative Windows runner; Bash scripts are thin wrappers.
- `QualityGate.cs` stays an Editor preflight; NUnit discovery/failure counts are parsed from XML after Unity exits.
- P1 is split into Engineering Gate, Mobile Release Readiness, and WebGL Public Deployment Readiness.
- `BLOCKED_NOT_VERIFIED` is a first-class result and must not be rewritten as PASS.

## Alternatives

- Single PASS/FAIL gate: rejected; it cannot represent missing devices, excluded platforms, or diagnostic runs.
- Treat missing evidence as advisory: rejected; it recreates the original "green but unverified" problem.
- Store evidence in ad-hoc paths: rejected; run-id and hashes are required for reproducibility.
```

- [ ] 步骤 2：验证

运行：`bash tools/check-stage0-consistency.sh`

预期：ADR-0002 相关检查通过；整体仍 FAIL。

- [ ] **步骤 3：Commit**

```bash
git add docs/architecture/adr-0002-quality-gate-evidence-model.md
git commit -m "docs: add quality gate evidence model ADR"
```

---

### 任务 5：ADR-0003 KCP 会话安全与 POC 计划

**文件：**
- 创建：`docs/architecture/adr-0003-kcp-session-security.md`
- 创建：`spikes/kcp-security-poc/README.md`

- [ ] **步骤 1：写 ADR（Status: Proposed until POC）**

```markdown
# ADR-0003: KCP Session Security

## Status

Proposed — POC required before Accepted. The mobile KCP public path is not release-ready until this ADR is Accepted and the POC evidence is attached.

## Context

KCP + Protobuf provides neither confidentiality nor peer authentication. The HMAC login ticket authenticates identity/integrity but does not encrypt the transport. Non-Development public mobile candidates therefore need a maintained DTLS 1.3 or Noise implementation integrated into both the Unity 2022.3 IL2CPP client and the .NET 8 server. Self-rolled cryptographic primitives are forbidden.

## Decision

- Select exactly one external provider after the POC. Candidate set: Noise.NET, Noise-C, wolfSSL DTLS, BouncyCastle DTLS, OpenSSL DTLS.
- Evaluation criteria: Unity 2022.3 IL2CPP Android/iOS compatibility, .NET 8 server support, license, maintenance status, package size, performance, and key-lifecycle fit.
- The ADR must record: protocol/library version, license, client/server integration points, identity and key source, bidirectional key derivation, AEAD, nonce/sequence handling, replay window, reconnect/rekey/rotation, downgrade rejection, secret cleanup, and redacted observability.
- Server identity must bind to the original `NetworkEndpoint` hostname, not a DNS-resolved IP.
- Development/loopback/controlled test networks may explicitly enable plaintext KCP; non-Development public candidates must fail closed.
- If the POC fails, the fallback is mobile WebSocket/WSS for public candidates; the fallback must be recorded here and the mobile KCP public path remains `BLOCKED_NOT_VERIFIED`.

## POC Acceptance

- .NET 8 server and Unity 2022.3 Android IL2CPP client complete the selected provider handshake.
- One AEAD message roundtrip succeeds.
- Tampered and replayed messages are rejected.
- License, maintenance, package size, and IL2CPP compatibility evidence are recorded.
- Evidence is stored in `spikes/kcp-security-poc/RESULT.md` and referenced here.
- A physical Android device is not required for the Stage 0 POC; emulator evidence is enough to accept the library, but not enough for Mobile Release Readiness.

## Consequences

- Task 13 implements only the provider interface, configuration fail-fast, and plaintext release guard.
- Task 32 implements the selected provider, key rotation drill, and security tests.
- Mobile Release Readiness cannot pass while this ADR is Proposed or the POC has failed.

## Alternatives

- All mobile traffic uses WSS: valid fallback; rejected as the primary path only if the POC succeeds.
- Plaintext KCP with HMAC tickets: rejected; HMAC does not provide confidentiality or peer authentication.
- Self-rolled encryption: rejected by the original spec and this ADR.
```

- [ ] **步骤 2：写 POC 执行说明**

`spikes/kcp-security-poc/README.md`：

```markdown
# KCP Session Security POC

This spike is not part of any Unity assembly or server project. It exists only to select and verify the ADR-0003 provider.

## Candidate matrix

| Candidate | Unity IL2CPP Android/iOS | .NET 8 server | License | Maintenance | Notes |
|---|---|---|---|---|---|
| Noise.NET | to verify | to verify | to verify | to verify | pure managed candidate |
| Noise-C | to verify | to verify | to verify | to verify | native binding candidate |
| wolfSSL DTLS | to verify | to verify | to verify | to verify | commercial/native candidate |
| BouncyCastle DTLS | to verify | to verify | to verify | to verify | managed candidate |
| OpenSSL DTLS | to verify | to verify | to verify | to verify | native candidate |

## Steps

1. For each candidate, record package URL/version, license, last release date, open security issues, and Unity IL2CPP notes.
2. Reject candidates that cannot be consumed by Unity 2022.3 Android IL2CPP or .NET 8.
3. Pick one candidate and build `Server` and `Client` .NET 8 console projects under this directory.
4. Implement handshake + AEAD roundtrip + tamper/replay rejection using the selected library's official sample.
5. Build a minimal Unity 2022.3 Android IL2CPP player that uses the same library; run on an emulator and capture logcat/build SHA/ABI.
6. Write `RESULT.md` with candidate matrix, selected library/version/license, commands, outputs, and PASS/FAIL/BLOCKED status.
7. If the POC fails, update ADR-0003 with the WSS fallback and mark the mobile KCP public path `BLOCKED_NOT_VERIFIED`.
```

- [ ] **步骤 3：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：ADR-0003 与 POC 目录相关检查通过；`spikes/kcp-security-poc/RESULT.md` 仍缺失，整体仍 FAIL。

- [ ] **步骤 4：Commit**

```bash
git add docs/architecture/adr-0003-kcp-session-security.md spikes/kcp-security-poc/README.md
git commit -m "docs: add kcp session security ADR and poc plan"
```

---

### 任务 6：执行 ADR-0003 POC

**文件：**
- 创建/修改：`spikes/kcp-security-poc/Server/`、`spikes/kcp-security-poc/Client/`
- 创建：`spikes/kcp-security-poc/UnityPoc/`（最小 Unity 工程或场景说明）
- 创建：`spikes/kcp-security-poc/RESULT.md`
- 修改：`docs/architecture/adr-0003-kcp-session-security.md`（Status/Accepted 或 fallback）

- [ ] **步骤 1：完成候选评估**

按 `spikes/kcp-security-poc/README.md` 的矩阵逐项记录：包版本、许可证、最近发布日期、维护状态、IL2CPP/Android 兼容性、.NET 8 兼容性。把矩阵写入 `RESULT.md`，并把不可行候选标为 `REJECTED` 及原因。

- [ ] **步骤 2：创建 .NET 8 POC 项目**

```bash
cd spikes/kcp-security-poc
dotnet new console -n Server -f net8.0
dotnet new console -n Client -f net8.0
dotnet new sln -n KcpSecurityPoc
dotnet sln add Server/Server.csproj Client/Client.csproj
```

在 `Server`/`Client` 中加入选定 provider 的 NuGet 或原生依赖。实现：

- 服务端监听 UDP 端口；
- 客户端连接并完成 provider 握手；
- 客户端发送一条固定字节消息，服务端解密后回传；
- 篡改消息、重放消息、错误服务端身份各触发一次拒绝；
- 输出每条用例的 PASS/FAIL、耗时和库版本。

把命令和输出保存到 `RESULT.md`。POC 代码只允许放在 `spikes/`，不得被 `Assets/` 或 `Server/` 引用。

- [ ] **步骤 3：构建 Unity Android IL2CPP POC**

创建最小 Unity 2022.3 工程或在现有工程内使用一个隔离场景，引用同一个 provider，连接本机 .NET 8 POC 服务端。POC 不得依赖任务 29 的 `MobileBuildScript`；如果现有工程 Android 构建入口尚未就绪，使用独立最小 Unity 工程。用 `-buildTarget Android` 构建 ARM64 IL2CPP，部署到 Android emulator，完成一次握手和消息往返。保存：

- 构建命令、Unity 版本、ABI、build SHA；
- `adb logcat` 中的握手/往返/拒绝结果；
- 失败时的完整异常。

若 emulator 不可用，记录 `BLOCKED_NOT_VERIFIED` 及原因；不得用桌面 Editor 结果代替 Unity IL2CPP 证据。

- [ ] **步骤 4：写 RESULT.md 并更新 ADR**

`RESULT.md` 必须包含以下字段，值由 POC 实际结果填写：

- Date
- Status: PASS / FAIL / BLOCKED_NOT_VERIFIED
- Selected provider（名称、版本、许可证）
- .NET 8 server 结果
- Unity 2022.3 Android IL2CPP 结果
- Handshake / AEAD roundtrip / Tamper rejection / Replay rejection 结果
- 证据路径（日志、截图、构建产物）
- Fallback（WSS 或 none）

把 ADR-0003 的 `## Status` 改为 `Accepted — 2026-11-07`（POC 通过）或 `Proposed — POC failed; fallback WSS`（POC 失败）。在 ADR 中引用 `RESULT.md`。

- [ ] **步骤 5：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：POC 相关检查通过；若 POC 失败，`stage-0-report.md` 必须记录 `BLOCKED_NOT_VERIFIED`，阶段 0 不得宣称移动 KCP 公网安全可用。

- [ ] **步骤 6：Commit**

```bash
git add spikes/kcp-security-poc docs/architecture/adr-0003-kcp-session-security.md
git commit -m "spike: verify kcp session security provider"
```

---

### 任务 7：修订原 Spec §6（平台矩阵、端点、安全、生命周期契约）

**文件：**
- 修改：`docs/superpowers/specs/2026-11-05-p0-p1-quality-improvement-design.md`

- [ ] **步骤 1：替换 §6.0 传输默认值**

删除“近期公开测试所有平台默认 WSS / KCP 默认禁用”的段落，写入执行设计 §2.4 的平台矩阵：

- Android/iOS：KCP + Protobuf；公网候选包叠加 ADR-0003 安全层；不得同时启用 WebSocket 主传输。
- WebGL：WebSocket；公网为 WSS；不得编译/打包/启用 KCP 玩家路径。
- Editor/Windows 测试：按 lane 显式选择；Native KCP lane 与 WebSocket/WebGL lane 分开报告。

同时把“如果改变主传输，必须更新 ADR/Spec/Gate”写回 §6.0。

- [ ] **步骤 2：加入端点与安全契约**

把执行设计 §4.2.2 的以下内容写入原 Spec §6（新增小节 `### 6.0.4 端点与公网安全契约`）：

- 唯一构造边界 `NetworkTransportFactory`；
- `NetworkEndpoint` 携带 transport kind/host/port/完整 WebSocket Uri；
- KCP 域名/IPv4/IPv6、DNS 候选去重、candidate race（250ms、最多 2 个、单候选 3s、总 10s）；
- 原始 hostname 绑定服务端身份；
- KCP + Protobuf 不提供机密性；ADR-0003 provider；非 Development 明文 fail-closed；
- WebGL KCP plugin 排除与产物检查。

- [ ] **步骤 3：加入资源与 UI 生命周期契约**

把执行设计 §4.2.1 的 Addressables 生产验证编排、`ResourceManager.ShutdownAsync` 与 `ResourceScope.CloseAsync` 分离、UI failure Presenter 注册、VfxCatalog 初始化、IL2CPP 协议 fixture 写入原 Spec §6.3 或新增 `### 6.3.1 生命周期契约`。

- [ ] **步骤 4：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：原 Spec 的平台矩阵、状态名、release-scope 检查通过；整体仍 FAIL。

- [ ] **步骤 5：Commit**

```bash
git add docs/superpowers/specs/2026-11-05-p0-p1-quality-improvement-design.md
git commit -m "docs(spec): add platform transport and lifecycle contracts"
```

---

### 任务 8：修订原 Spec §7（P0 设计）

**文件：**
- 修改：`docs/superpowers/specs/2026-11-05-p0-p1-quality-improvement-design.md`

- [ ] **步骤 1：更新 P0-0 测试入口**

把 P0-0 改为执行设计 §5.2 的模型：原生 CLI runner、`-buildTarget StandaloneWindows64`、三套 XML/日志、server profile、0 tests 失败、WebGL compile/build lane、run-id、manifest、dirty worktree 只产生 `DIAGNOSTIC_ONLY`。删除“MCP tests-run 作为门禁”的描述。

- [ ] **步骤 2：更新 P0-1 至 P0-7**

逐项写入执行设计 §4.1/§4.3：

- P0-1 Bag：保留原任务 2 的验收；增加“未初始化写操作返回明确结果”。
- P0-2 技能/EventBus：增加 U-P0-05（sealed class、Interlocked、copy-on-write、ResetAll、SubsystemRegistration）。
- P0-4 移动/防御/精力：增加 U-P0-06（删除 `Thread.Sleep`、可注入时钟、fake transport）。
- P0-5 登录：增加 task 6 的完整验收（HMAC 票据、开发票据、ProtocolVersion 2、断线幂等）。
- P0-7 客户端邮箱：增加 U-P0-07（control 优先 drain、ClientEnvelope/DrainResult/LoginResult、Host CTS、异常观察）。

- [ ] **步骤 3：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：原 Spec 的 P0 相关检查通过；整体仍 FAIL。

- [ ] **步骤 4：Commit**

```bash
git add docs/superpowers/specs/2026-11-05-p0-p1-quality-improvement-design.md
git commit -m "docs(spec): apply p0 review revisions"
```

---

### 任务 9：修订原 Spec §8–§10、§13（P1、阶段、Gate、治理）

**文件：**
- 修改：`docs/superpowers/specs/2026-11-05-p0-p1-quality-improvement-design.md`

- [ ] **步骤 1：更新 P1-1 至 P1-6**

写入执行设计 §4.2 的 U-P1-01..13 映射，并按执行设计 §3.6 的归属矩阵拆分 Gate。重点：

- P1-3 资源/UI：U-P1-03/04/05/06/11；
- P1-4 移动性能：U-P1-10/12/13；
- P1-5 构建/CI：U-P1-06/08/09/10；
- P1-6 可观测性：KCP security provider、WSS、健康检查、优雅停机。

- [ ] **步骤 2：更新 §9 阶段与 §10 门禁**

把 §9 的 P1-A/P1-B/P1-C 拆成 P1-A、P1-B1、P1-B2、P1-C1、P1-C2；把 §10.2 的 P1 Gate 拆成 P1 Engineering Gate、Mobile Release Readiness、WebGL Public Deployment Readiness，并写入执行设计 §3.5 的状态机与 §3.6 的验收归属表。

- [ ] **步骤 3：更新 §13 治理**

加入：`release-scope.yaml` 是 blocking 平台的唯一来源；每个 Gate 生成 run-id 报告；例外必须含 owner/expiresAt/恢复条件；未执行项保持 `BLOCKED_NOT_VERIFIED`；行为变化同步模块 README。

- [ ] **步骤 4：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：原 Spec 的 P1/Gate/治理检查通过；整体仍 FAIL。

- [ ] **步骤 5：Commit**

```bash
git add docs/superpowers/specs/2026-11-05-p0-p1-quality-improvement-design.md
git commit -m "docs(spec): split p1 gates and add governance rules"
```

---

### 任务 10：修订原计划全局约束、波次表与任务 1

**文件：**
- 修改：`docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md`

- [ ] **步骤 1：更新全局约束与波次表**

在全局约束中加入：阶段 0 先行；平台传输矩阵；禁止 `git add .`；dirty worktree 只产生 `DIAGNOSTIC_ONLY`；任务级证据使用 run-id。

把波次表替换为：

| 波次 | 任务 | 退出条件 |
|---|---|---|
| 阶段 0 | 文档修订 | 阶段 0 退出清单全绿 |
| Gate 0 | 1 | runner/发现/server profile/XML/失败传播可信 |
| P0-A | 2–4 | Bag、伤害上下文、EventBus/技能事务 |
| P0-B | 5–8 | 单世界、幂等登录、移动、格挡精力 |
| P0-C | 9–13 | 有界队列、准入背压、KCP、客户端邮箱 |
| P0 Gate | 14 | 原 Spec 10.1 + 复核 7.1 |
| P1-A | 15–18 | WorldInstance/AOI/ORCA/导航 |
| P1-B1 | 19–23 | 伤害管线、Sys3C 注入、资源、Addressables/UI |
| P1-B2 | 24–28 | 性能、HitStop、VFX、Monster 池 |
| P1-C1 | 29–31 | 移动构建、架构守卫、CI/Coverage |
| P1-C2 | 32 | 可观测性、WSS、KCP 安全、优雅停机 |
| P1 Engineering Gate | 33 | 工程项 |
| Mobile Release Readiness | 33 | 设备证据 |
| WebGL Public Deployment Readiness | 33 | 公网 WSS/浏览器证据 |

- [ ] **步骤 2：重写任务 1 文件清单**

把任务 1 的文件清单替换为执行设计 §5.1：

- 新建 `Assets/Scripts/Editor/Quality/GameSys.EditorQuality.asmdef`、`QualityGate.cs`、`AssemblyInfo.cs`；
- 修改 `Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`；
- 新建 `Assets/Scripts/Tests/Editor/Quality/QualityGateTests.cs`、`Assets/Scripts/Tests/Editor/Architecture/PlayModeAssemblyContractTests.cs`；
- 移动 `Assets/Scripts/Tests/PlayMode/KcpNet.PlayModeTests.asmdef` → `GameSys.PlayModeTests.asmdef`（改程序集名、移除 `KcpNet` 引用和 `!UNITY_WEBGL`）；
- 新建 `Assets/Scripts/Tests/PlayMode/NativeNetwork/KcpNet.NativePlayModeTests.asmdef`、`TestServerEndpoint.cs`、`NativeServerProfileSentinelTests.cs`；
- 移动 12 个 Native 测试文件（`AdapterConnectTests.cs`、`AoiWorldTests.cs`、`BlockMitigationE2ETests.cs`、`ClientRespawnWaitTests.cs`、`CombatSyncTests.cs`、`EchoSmokeTests.cs`、`HitJudgmentAlignmentTests.cs`、`LoginEnterWorldTests.cs`、`MonsterWorldTests.cs`、`MovementSyncTests.cs`、`ThreeClientCombatConsistencyTests.cs`、`TwoClientWorldTests.cs`）及各自 `.meta`；
- 新建 `tools/quality-gate.ps1`、`tools/tests/quality-gate-selftest.ps1`、`tools/quality-gate.sh`、`tools/unity-tests-mcp.sh`；
- 修改 `tools/unity-tests.sh`；
- 修改/重新生成 `production/quality/test-manifest.json`、`production/quality/gate-exceptions.json`（阶段 0 已建立基线，任务 1 迁移测试后重新生成，不重复创建）；
- 修改 `docs/agent-token-hygiene.md`。

- [ ] **步骤 3：重写任务 1 实现步骤**

写入执行设计 §5.2 的 9 条实现要点和 §5.3 的验收：

1. `QualityGate.cs` 只做 Editor preflight（active target、包名、asmdef 契约），提供 EditMode 可调用的纯验证器；不切 BuildTarget，不判断 NUnit 最终发现数。
2. `quality-gate.ps1` 在 Unity 退出后解析 XML，验证 discovered/failed/skipped/manifest/退出码/timeout/新鲜度/hash。
3. 原生 runner 启动独立 Unity 进程，传 `-buildTarget StandaloneWindows64`；EditMode、通用 PlayMode、每个 Native profile 各自 XML/日志。
4. 服务端先 `dotnet build Server/Server3C.csproj -c Debug`，再 `dotnet Server/bin/Debug/net8.0/Server3C.dll <port> <profile args>`；记录 PID、动态 TCP/UDP 端口、ready 日志 + 传输 probe。
5. Native profile：`native-clean`、`native-bots`（`AoiWorldTests`，`--bots 100 --bots-radius 500`）、`native-monsters`（`MonsterWorldTests`/`HitJudgmentAlignmentTests`，`--monsters 4`）；fixture 默认独立 server 进程。
6. `test-manifest.json` 保存每个 assembly/profile 的测试全名；未解释下降/skip/ignore/inconclusive 失败。
7. WebGL 独立 `-buildTarget WebGL` 进程；区分 compile contract、Player Build、runtime smoke。
8. `quality-gate-selftest.ps1` 用 stub/fixture XML 证明 server ready 超时、Unity 非零退出、XML 缺失/损坏、manifest 下降、过期 exception、stale artifact、teardown 超时/残留子进程均非零。
9. `--infra`/`--baseline` 在 dirty worktree 只输出 `DIAGNOSTIC_ONLY`；`--p0`/`--p1`/Release 要求 clean worktree。
10. Gate 0 运行 `Assets/Scripts/Tests/Editor/Network/ProtocolRoundtripTests.cs`、`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs` 与服务端协议测试；缺跨端一致性断言时在任务 1 补一个双端 roundtrip。`quality-gate.ps1` 是 Windows 权威入口；Linux 默认只做内容 hash/静态检查，若要在 Linux 跑 Unity Gate 必须显式提供 `pwsh` 或 POSIX runner。

- [ ] **步骤 4：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：原计划的 Gate 0/波次/关键字检查通过；整体仍 FAIL。

- [ ] **步骤 5：Commit**

```bash
git add docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md
git commit -m "docs(plan): add gate 0 infrastructure and revised waves"
```

---

### 任务 11：修订原计划 P0-A/P0-B（任务 2–8）

**文件：**
- 修改：`docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md`

- [ ] **步骤 1：任务 4 写入 U-P0-05**

在任务 4 的文件清单加入 `EventSubscription.cs`（或等价）及测试；在步骤中加入：

- `EventSubscription` 改为 `sealed class`；
- `Interlocked.Exchange` 保证重复 Dispose 幂等；
- `Emit` 使用 copy-on-write 快照，禁止热路径 `ToArray()`；
- `ResetAll` 清普通/定向订阅、暂停状态、空桶；
- `SubsystemRegistration` 重置；
- 测试 `Copied_Tokens_Dispose_Only_Once`、重复 Dispose 不抛异常、Emit 无临时数组。

- [ ] **步骤 2：任务 7 写入 U-P0-06**

在任务 7 的文件清单加入 `MovementReporter.cs` 及其测试；在步骤中加入：

- 删除 `injectedSendDelayMs` 和 `Thread.Sleep`；
- 把 `DateTime.UtcNow` 改为可注入时钟；
- 延迟/抖动/丢包移到 fake transport 或 test scheduler；
- 测试 `Injected_Network_Delay_Does_Not_Block_MainThread`。

- [ ] **步骤 3：任务 5/6/8 写入验收补齐**

把执行设计 §4.3 的“任务 5/6/8”行原文写入对应任务的验收步骤。任务 2/3 保持原验收，但在步骤末尾注明“P0-A 波次 Gate 必须覆盖原 Spec 10.1 的 Bag/DamageContext 项”。

- [ ] **步骤 4：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：原计划的 P0-A/B 关键字检查通过；整体仍 FAIL。

- [ ] **步骤 5：Commit**

```bash
git add docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md
git commit -m "docs(plan): apply p0-a p0-b review revisions"
```

---

### 任务 12：修订原计划 P0-C（任务 9–13）

**文件：**
- 修改：`docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md`

- [ ] **步骤 1：任务 9/10/11 写入验收补齐**

把执行设计 §4.3 的“任务 9/10/11”行原文写入对应任务验收步骤。任务 10 必须包含 Spec 6.0.2 的速率表、WS 64KiB、待发 256 帧/4MiB、慢客户端断开、分片 5s、未配置 `Encrypted/Compressed` 拒绝、单平台单主传输。

- [ ] **步骤 2：任务 12 写入 U-P0-07**

在任务 12 文件清单加入 `ClientEnvelope.cs`、`DrainResult.cs`、`LoginResult.cs`；在步骤中加入：

- `Update` 先 drain login/error/disconnect/control，再判断 InWorld；
- 邮箱容量 512、每帧 64 条或 2ms；WorldSnapshot 有界 FIFO（非 latest-wins）；可靠命令不静默丢；
- 溢出 fatal 标志 + 下一帧断线/重同步；
- `OnError`/`OnDisconnected`/`OnServerRespawn` 只在主线程触发；
- `ConnectAndLogin` 返回 `UniTask<LoginResult>`/`Task<LoginResult>` + CancellationToken；
- Host 持有连接 CTS 和 Task；`Disconnect`/`OnDestroy` 取消并观察异常；
- 测试 `Pump_Drains_LoginResponse_While_LoggingIn`、`Pump_Drains_Error_And_Disconnect_Before_InWorld`、Host 销毁取消。

- [ ] **步骤 3：任务 13 前移为 P0 并重写为平台传输/KCP 边界**

把任务 13 的优先级改为 `P0；依赖任务 10、11、12；位于任务 14 前`。文件清单至少包含：

- `ClientNetworkHost.cs`、`NetworkTransportFactory.cs`、`NetworkEndpoint.cs`、`KcpNetClientAdapter.cs`、`WebSocketClientAdapter.cs`、WebGL/.NET channel；
- 两份 `Kcp.dll` 及 `.meta`（`Assets/Plugins/ProtoBuf/Kcp.dll`、`Assets/Packages/Kcp.2.7.0/lib/netstandard2.1/Kcp.dll`）；
- `Assets/Scripts/AOT/KcpNet` 下 7 个未带 `#if !UNITY_WEBGL` 的文件：`IKcpTransport.cs`、`ILogger.cs`、`IMessageExecutor.cs`、`KcpOptions.cs`、`KcpSession.cs`、`MessageDispatcher.cs`、`KcpServerSession.cs`；
- `TransportFactoryBoundaryGuardTests`、`KcpPluginPlatformGuardTests`、WebGL 产物检查；
- security provider 接口、配置 fail-fast、plaintext release 守卫；
- 30 分钟 Win64 KCP soak（移动真机 soak 归 Mobile Release Readiness）。

任务步骤加入：

1. 只有 factory 可构造具体 transport；factory 外构造立即失败。
2. Android/iOS Player → KCP；WebGL → WebSocket；Editor/Windows 由 lane 显式选择，缺少选择 fail-fast。
3. `NetworkEndpoint` 携带完整 WebSocket Uri；KCP 域名/IPv4/IPv6 + DNS candidate race（250ms、最多 2、单 3s、总 10s）。
4. WebGL 排除 KCP plugin；`KcpNet` 在 WebGL 必须编译为空/安全程序集。
5. 两份 `Kcp.dll` 去重：已跟踪副本为权威；未跟踪副本先确认可再生且无用户改动；`Assets/Packages/` 归属决定写入文件清单。
6. 生产默认不再断言“KCP 全局禁用”；改为“每个平台/构建配置只启用一个主传输”；P0 不宣称移动真机/公网安全已验证。
7. 测试：连接、断开、重复释放、接收循环停止、factory 边界、plugin 平台守卫、WebGL 产物无 KCP。

- [ ] **步骤 4：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：任务 13 的 P0/关键字检查通过；`task 13 precedes task 14` 通过；整体仍 FAIL。

- [ ] **步骤 5：Commit**

```bash
git add docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md
git commit -m "docs(plan): promote kcp and platform boundary to p0"
```

---

### 任务 13：修订原计划任务 14（P0 Gate）

**文件：**
- 修改：`docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md`

- [ ] **步骤 1：重写任务 14 的 Gate 条件**

任务 14 必须证明：

- 服务端全量测试通过；
- Unity EditMode、通用 PlayMode、每个 Native profile 各自 discovered > 0 且通过；
- Native suite 从清洁环境自行启动/停止服务器；
- 登录响应不被状态判断阻塞；
- Unity 主线程无测试延迟阻塞；
- KCP 生命周期修复或明确隔离；生产默认 WSS/平台矩阵检查通过；
- WebGL compile contract 与 Player Build 结果独立，未执行 runtime smoke 时只报告 compile/build verified；
- 所有结果由本轮 XML/日志/退出码/manifest/产物 hash 证明。

- [ ] **步骤 2：更新报告路径与步骤**

把 `production/quality/p0-gate-2026-11.md` 改为 `production/quality/p0-gate-<run-id>.md`；加入 `bash tools/quality-gate.sh --p0`、`quality-gate-selftest.ps1`、test manifest、release scope 复制到 artifact、未执行项 `BLOCKED_NOT_VERIFIED`。

- [ ] **步骤 3：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：P0 Gate 相关检查通过；整体仍 FAIL。

- [ ] **步骤 4：Commit**

```bash
git add docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md
git commit -m "docs(plan): tighten p0 gate evidence"
```

---

### 任务 14：修订原计划 P1-A/P1-B1（任务 15–23）

**文件：**
- 修改：`docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md`

- [ ] **步骤 1：任务 15–18 增加 Gate 引用**

在任务 15–18 的验收中加入 P1-A 波次 Gate：两个 WorldInstance 隔离、AOI/ORCA/导航规模测试、跨实例不可见/不可命中/不可仇恨。

- [ ] **步骤 2：任务 19–23 写入 U-P1 修订**

按执行设计 §4.2 写入：

- 任务 19：U-P1-01（唯一伤害模型、迁移矩阵、`[Obsolete]` 适配、Prefab GUID 扫描）。
- 任务 20：U-P1-02（场景组合根、类型化 Prefab 根、Camera 注入、`PlayerSpawnedEvent` 携带 `Sys3CEntry`、失败清理）。
- 任务 21：U-P1-03/04/11（UniTask Addressables awaiter、删除 `.Task`/`WaitForCompletion`、SceneLease `UnloadAsync`、ResourceScope owner/ShutdownAsync/ResetForTests）。
- 任务 22：U-P1-06（AddressablesBuildScript、Use Existing Build、Analyze、CoreUI/CombatVFX group/schema/address）。
- 任务 23：U-P1-05/11/13（Presenter 注册 token、SubsystemRegistration、双入口场景、IScreenMetrics/ILowMemorySignal、移动生命周期）。

- [ ] **步骤 3：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：P1-A/B1 关键字检查通过；整体仍 FAIL。

- [ ] **步骤 4：Commit**

```bash
git add docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md
git commit -m "docs(plan): apply p1-a p1-b1 review revisions"
```

---

### 任务 15：修订原计划 P1-B2/P1-C1/P1-C2（任务 24–33）

**文件：**
- 修改：`docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md`

- [ ] **步骤 1：任务 24–28 写入 U-P1 修订**

- 任务 24/27：U-P1-10（性能方法、设备档位、原始数据）。
- 任务 25：局部 HitStop，不修改全局 `Time.timeScale`。
- 任务 26：U-P1-07（真实 `HitSparkBurst`/Heal、VfxCatalog、AssetLease、preload、lease 回收、GUID 检查）。
- 任务 28：U-P1-12（Prepare→Activate→NavMesh→Warp→isOnNavMesh→AI；失败回池）。

- [ ] **步骤 2：任务 29–33 写入 U-P1 修订**

- 任务 29：U-P1-08/09/13（`-buildTarget Android`/`iOS`、release scope、BuildScenes、BuildReport/APK/AAB/Xcode、IL2CPP device smoke、link.xml/Managed Stripping Level）。
- 任务 30：保留并扩展任务 13 的 factory/plugin/产物守卫；新增 GetComponent/FindObject、Resources.Load、asmdef 反向引用守卫；守卫稳定后再拆 Bag/Stamina。
- 任务 31：U-P1-06/08/10（CI、Coverage 参数、Addressables Existing Build、artifact 校验、HAS_IOS_RUNNER、release-scope）。
- 任务 32：U-P1 可观测性 + KCP security provider、密钥轮换、优雅停机、Caddy WSS。
- 任务 33：P1 Engineering Gate + Mobile Release Readiness + WebGL Public Deployment Readiness；按执行设计 §7.2/7.3/7.4 判定。

- [ ] **步骤 3：验证**

运行：`bash tools/check-stage0-consistency.sh`

预期：P1-B2/C1/C2 关键字检查通过；整体仍 FAIL。

- [ ] **步骤 4：Commit**

```bash
git add docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md
git commit -m "docs(plan): apply p1-b2 p1-c review revisions"
```

---

### 任务 16：生成生产治理文件

**文件：**
- 创建：`production/quality/release-scope.yaml`
- 创建：`production/quality/gate-exceptions.json`
- 创建：`production/qa/device-profiles/android-reference.yaml`
- 创建：`tools/generate-test-manifest.py`
- 生成：`production/quality/test-manifest.json`

- [ ] **步骤 1：写 release scope**

`production/quality/release-scope.yaml`：

```yaml
milestone: p0-p1-quality-2026-11
blockingPlatforms:
  - android
  - webgl
requiredDeviceLanes:
  - android-reference
requiredBrowserLanes:
  - chromium-desktop
publicEndpoints: []
approver: jincaizi
approvedAt: "2026-11-07"
sourceGitSha: ""
```

运行以下命令写入实际 SHA：

```bash
SHA="$(git rev-parse HEAD)"
sed -i "s/^sourceGitSha:.*/sourceGitSha: \"$SHA\"/" production/quality/release-scope.yaml
```

WebGL 纳入 blocking；P0/P1 Engineering 仍验证 WebGL compile/WS lane；WebGL Public Deployment Readiness 需要公开域名、证书、反向代理和 `requiredBrowserLanes` 证据，缺失时 `BLOCKED_NOT_VERIFIED`。当前 `publicEndpoints` 为空，因此阶段 0 不得宣称 WebGL 公网准备完成。

- [ ] **步骤 2：写 gate exceptions 与 Android profile**

`production/quality/gate-exceptions.json`：

```json
{
  "exceptions": []
}
```

`production/qa/device-profiles/android-reference.yaml`：

```yaml
configured: false
deviceModel: ""
soc: ""
ramGb: 0
os: ""
resolution: ""
qualityLevel: ""
targetFps: 30
warmupSeconds: 120
sampleSeconds: 1800
p95Algorithm: "nearest-rank"
rawDataFormat: "csv: frame,ms"
```

`configured: false` 是显式未配置状态；P1-B2 前未填实际设备时，性能 Gate 为 `BLOCKED_NOT_VERIFIED`。

- [ ] **步骤 3：写 test manifest 生成器**

`tools/generate-test-manifest.py`：

```python
#!/usr/bin/env python3
"""Generate production/quality/test-manifest.json from Unity test sources."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "production/quality/test-manifest.json"

TEST_ATTR = re.compile(r"\[(?:Test|UnityTest|TestCase)(?:\([^\]]*\))?\]")
METHOD = re.compile(r"(?:public|internal)\s+(?:async\s+)?(?:void|IEnumerator|Task)\s+(\w+)\s*\(")
CLASS = re.compile(r"\b(?:public|internal)\s+(?:sealed\s+)?class\s+(\w+)")
NAMESPACE = re.compile(r"\bnamespace\s+([\w.]+)")

def parse_file(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8", errors="replace")
    ns = NAMESPACE.search(text)
    cls = CLASS.search(text)
    if not cls:
        return []
    prefix = (ns.group(1) + "." if ns else "") + cls.group(1) + "."
    names: list[str] = []
    for m in TEST_ATTR.finditer(text):
        tail = text[m.end():m.end() + 400]
        mm = METHOD.search(tail)
        if mm:
            names.append(prefix + mm.group(1))
    return names

def profile_for(path: Path, assembly: str) -> str:
    if assembly == "GameSys.EditorTests":
        return "editmode"
    if assembly == "GameSys.PlayModeTests":
        return "generic"
    stem = path.stem
    if stem == "AoiWorldTests":
        return "native-bots"
    if stem in {"MonsterWorldTests", "HitJudgmentAlignmentTests"}:
        return "native-monsters"
    return "native-clean"

def main() -> int:
    groups: dict[str, dict[str, set[str]]] = {}
    editor = ROOT / "Assets/Scripts/Tests/Editor"
    play = ROOT / "Assets/Scripts/Tests/PlayMode"
    native = play / "NativeNetwork"

    for path in editor.rglob("*.cs"):
        for name in parse_file(path):
            groups.setdefault("GameSys.EditorTests", {}).setdefault("editmode", set()).add(name)
    native_files = {
        "AdapterConnectTests", "AoiWorldTests", "BlockMitigationE2ETests",
        "ClientRespawnWaitTests", "CombatSyncTests", "EchoSmokeTests",
        "HitJudgmentAlignmentTests", "LoginEnterWorldTests", "MonsterWorldTests",
        "MovementSyncTests", "ThreeClientCombatConsistencyTests", "TwoClientWorldTests",
    }
    for path in play.rglob("*.cs"):
        if native in path.parents:
            continue
        asm = "KcpNet.NativePlayModeTests" if path.stem in native_files else "GameSys.PlayModeTests"
        prof = profile_for(path, asm) if asm == "KcpNet.NativePlayModeTests" else "generic"
        for name in parse_file(path):
            groups.setdefault(asm, {}).setdefault(prof, set()).add(name)
    if native.exists():
        for path in native.rglob("*.cs"):
            if path.name.startswith("TestServerEndpoint") or "Sentinel" in path.name:
                continue
            prof = profile_for(path, "KcpNet.NativePlayModeTests")
            for name in parse_file(path):
                groups.setdefault("KcpNet.NativePlayModeTests", {}).setdefault(prof, set()).add(name)
    else:
        groups.setdefault("KcpNet.NativePlayModeTests", {}).setdefault("native-clean", set())

    assemblies: dict[str, dict] = {}
    for asm, profiles in sorted(groups.items()):
        cohorts: dict[str, dict] = {}
        for prof, names in sorted(profiles.items()):
            cohorts[prof] = {
                "serverArgs": [],
                "testFilter": sorted(names),
                "fixtures": sorted({n.rsplit(".", 1)[0] for n in names}),
                "timeoutSeconds": 300,
            }
        assemblies[asm] = {
            "profile": "editmode" if asm == "GameSys.EditorTests" else "playmode",
            "cohorts": cohorts,
        }

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"generatedAt": "2026-11-07", "assemblies": assemblies}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    total = sum(len(c["testFilter"]) for a in assemblies.values() for c in a["cohorts"].values())
    print(f"wrote {OUT} ({len(assemblies)} assemblies, {total} tests)")
    return 0

if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **步骤 4：生成并验证 manifest**

```bash
python tools/generate-test-manifest.py
python -m json.tool production/quality/test-manifest.json >/dev/null
bash tools/check-stage0-consistency.sh
```

预期：manifest 有效，包含至少 3 个 assembly（`GameSys.EditorTests`、`GameSys.PlayModeTests`、`KcpNet.NativePlayModeTests`）；整体仍 FAIL。这是 Stage 0 基线；Gate 0 任务 1 迁移测试后必须重新运行生成器并提交更新。

- [ ] **步骤 5：Commit**

```bash
git add production/quality/release-scope.yaml production/quality/gate-exceptions.json \
  production/qa/device-profiles/android-reference.yaml tools/generate-test-manifest.py \
  production/quality/test-manifest.json
git commit -m "chore(quality): add release scope and test manifest"
```

---

### 任务 17：阶段 0 退出验证与激活

**文件：**
- 修改：`docs/superpowers/specs/2026-11-07-p0-p1-quality-improvement-execution-design.md`
- 创建：`production/quality/stage-0-report.md`

- [ ] **步骤 1：运行完整一致性检查**

```bash
bash tools/sync-technical-preferences.sh
bash tools/check-stage0-consistency.sh
```

预期：`STAGE 0 CHECK: PASS`。若仍有 FAIL，逐项修复；不得跳过。

- [ ] **步骤 2：记录 Git SHA 并更新激活表**

运行：

```bash
git rev-parse HEAD
git rev-parse HEAD:docs/superpowers/specs/2026-11-05-p0-p1-quality-improvement-design.md
git rev-parse HEAD:docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md
git rev-parse HEAD:docs/architecture/adr-0001-platform-transport-matrix.md
git rev-parse HEAD:docs/architecture/adr-0002-quality-gate-evidence-model.md
git rev-parse HEAD:docs/architecture/adr-0003-kcp-session-security.md
git rev-parse HEAD:production/quality/release-scope.yaml
```

把输出填入执行设计 §8 的“激活记录”表，替换所有“待阶段 0 填写”。把状态改为“已确认，执行基线”。

- [ ] **步骤 3：写阶段 0 工期重估与报告**

先写 `production/quality/stage-0-estimate.md`，按波次列出 Revision 2 范围的估算（人天、假设、未覆盖项）。必须明确说明原 49–86 人天只覆盖原 P0/P1 工程部分，不得作为 Revision 2 的完整估算。

然后生成 `production/quality/stage-0-report.md`：

```bash
RUN_ID="stage0-$(date -u +%Y%m%dT%H%M%SZ)-$(git rev-parse --short HEAD)"
mkdir -p "production/quality/artifacts/$RUN_ID"
SHA="$(git rev-parse HEAD)"
HASH="$(sha256sum docs/technical-preferences.md | awk '{print $1}')"
POC_STATUS="$(grep -m1 '^Status:' spikes/kcp-security-poc/RESULT.md | sed 's/^Status:[[:space:]]*//' | tr -d '\r' | xargs)"
cat > production/quality/stage-0-report.md <<EOF
# Stage 0 Report

- Date: 2026-11-07
- Run ID: $RUN_ID
- Source branch: codex/quality/stage-0
- Base Git SHA: $SHA
- Checker: bash tools/check-stage0-consistency.sh -> PASS
- Technical preferences marker: $HASH
- ADR-0003 POC: $POC_STATUS
- Mobile KCP public security: $( [ "$POC_STATUS" = "PASS" ] && echo PASS || echo BLOCKED_NOT_VERIFIED )
- WebGL public deployment: BLOCKED_NOT_VERIFIED until public endpoint/browser lane evidence exists
- iOS: NOT_IN_SCOPE (no macOS runner)
- Estimate: production/quality/stage-0-estimate.md
- Unexecuted items: none beyond the statuses above
- Evidence paths: production/quality/artifacts/$RUN_ID/
EOF
```

不得把未执行的 POC、设备或公网证据写成 PASS。

- [ ] **步骤 4：最终一致性检查**

```bash
bash tools/check-stage0-consistency.sh
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add docs/superpowers/specs/2026-11-07-p0-p1-quality-improvement-execution-design.md \
  production/quality/stage-0-report.md
git commit -m "docs: activate stage 0 baseline"
```

- [ ] **步骤 6：阶段 0 完成判定**

只有以下全部满足才算完成：

- `tools/check-stage0-consistency.sh` 退出 0；
- 三份 ADR 存在且 ADR-0003 记录了 POC 结果；
- 原 Spec 与计划不再有旧 WSS 默认、旧 Gate 结构和旧任务 13 优先级；
- `release-scope.yaml`、`test-manifest.json`、`gate-exceptions.json`、Android profile 存在；
- 激活表全部填 Git SHA；
- 阶段 0 报告记录未执行项，且没有把 `BLOCKED_NOT_VERIFIED` 写成 PASS。

若 ADR-0003 POC 为 FAIL：阶段 0 不能标记完成。停止并请求用户确认 WSS 回退或更换候选重跑 POC；不得把 `BLOCKED_NOT_VERIFIED` 写成 PASS，也不得进入 Gate 0 的移动 KCP 公网路径。

---

## 自检

### 1. 规格覆盖度

| 执行设计章节 | 本计划任务 |
|---|---|
| §2.4 平台传输矩阵 | 任务 3、7、12 |
| §3.5 Gate 状态机 | 任务 4、9 |
| §3.6 Gate 验收归属 | 任务 4、9、15 |
| §4.1 U-P0-01..07 | 任务 10、11、12 |
| §4.2 U-P1-01..13 | 任务 14、15 |
| §4.3 原计划任务验收补齐 | 任务 11、12、15 |
| §4.4 Gate 条件固化 | 任务 10、13 |
| §4.2.1 生命周期契约 | 任务 7 |
| §4.2.2 平台传输/端点/安全 | 任务 5、6、7、12 |
| §4.5 治理文件与脚本 | 任务 2、3、4、5、16 |
| §5 Gate 0 设计 | 任务 10 |
| §7 完成判定 | 任务 13、15、17 |
| §8 阶段 0 退出清单 | 任务 17 |
| §2.1–2.3 范围/冻结、§3.2–3.3 执行模型 | 全局约束、任务 17 |

### 2. 占位符扫描

计划中没有未决占位符。唯一的动态值是阶段 0 完成时由 `git rev-parse` 和 `sha256sum` 生成的 SHA/哈希；它们在任务 17 有明确命令，不是未决设计。

### 3. 类型与命名一致性

- 所有 Gate 状态名在 ADR-0002、原 Spec、原计划、阶段 0 报告中一致：`PASS`、`PASS_WITH_ADVISORIES`、`FAIL`、`BLOCKED_NOT_VERIFIED`、`NOT_IN_SCOPE`、`DIAGNOSTIC_ONLY`。
- 所有构建目标名统一为 `StandaloneWindows64`、`Android`、`iOS`、`WebGL`。
- `TestServerEndpoint`、`TransportFactoryBoundaryGuardTests`、`KcpPluginPlatformGuardTests`、`VfxCatalog`、`SceneLease`、`HAS_IOS_RUNNER` 在计划与原任务中使用同一名称。
- 技术偏好生成文件路径统一为 `.claude/docs/technical-preferences.md` 与 `.codex/docs/technical-preferences.md`。

---

## 执行交接

计划已保存到 `docs/superpowers/plans/2026-11-07-p0-p1-quality-stage-0.md`。

执行方式：

1. **子代理驱动（推荐）**：每个任务调度一个新的子代理，任务间进行审查，快速迭代。必需子技能：`superpowers:subagent-driven-development`。
2. **内联执行**：在当前会话中使用 `superpowers:executing-plans` 执行任务，批量执行并设有检查点。

选择哪种方式？

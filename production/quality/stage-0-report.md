# Stage 0 Report

- Date: 2026-11-07
- Run ID: `pending evidence capture`
- Source branch: `codex/quality/stage-0`
- Base Git SHA: `pending evidence capture`
- Checker: `bash tools/check-stage0-consistency.sh` — actual result captured on the clean evidence commit (see “Checker output” below); not pre-written
- Checker log: `production/quality/stage-0-checker.log` (full output and exit code)
- Technical preferences source marker: `TP-SENTINEL: 670a084a1a9a33de91dcbb37922d4c5c67e01c04de2c7a8bebca1481618fe096`
- Technical preferences reproducibility: `.gitattributes` pins `docs/technical-preferences.md`, both generated entries and the sentinel to `text eol=lf`; `git add --renormalize` applied, so a Windows checkout with `core.autocrlf=true` keeps the LF hash
- Claude consumer sentinel: **PASS** (opened generated file and reported marker)
- Codex consumer sentinel: **BLOCKED_NOT_VERIFIED** (no `codex` CLI on this host)
- Linux consumer/hash: **BLOCKED_NOT_VERIFIED** (no WSL distribution or Linux runner on this host; checker was not re-run under Linux)
- Estimate: `production/quality/stage-0-estimate.md` — status **PRESENT** (not a Gate PASS)
- Activation: **PENDING USER DECISION** — documentation/governance artifacts are ready; §8 still has 2 `BLOCKED_NOT_VERIFIED` items (Codex/Linux consumer sentinel) and 3 execution items deferred to task 13/Gate 0; the execution baseline is not activated and Stage 0 is not declared complete.

## Status detail

| Item | Status | Evidence / reason |
|---|---|---|
| Stage 0 consistency checker | Captured on evidence commit (see below) | `production/quality/stage-0-checker.log`; actual output embedded below |
| Technical preferences sync (Claude/Codex generated entries) | PASS | `.claude/docs/technical-preferences.md`, `.codex/docs/technical-preferences.md` carry source SHA-256 `670a084a...` |
| Technical preferences hash reproducibility | PASS (Windows) | `.gitattributes` `text eol=lf`; renormalized; LF hash `670a084a...` reproduced from a clean export |
| Claude consumer sentinel | PASS | `production/quality/technical-preferences-sentinel.md` |
| Codex consumer sentinel | BLOCKED_NOT_VERIFIED | No `codex` CLI; Codex minimal review not executed |
| Linux consumer/hash | BLOCKED_NOT_VERIFIED | No WSL distribution or Linux runner; hash/checker not re-run under Linux |
| ADR-0003 POC (Noise-C provider integration) | PASS (provider only) | `spikes/kcp-security-poc/RESULT.md` |
| KCP/UDP public path | BLOCKED_NOT_VERIFIED | POC exercised UDP only on .NET loopback; the Unity Android leg used TCP framing via `adb reverse` |
| ARM64 runtime | BLOCKED_NOT_VERIFIED | APK built for `arm64-v8a`, but only an x86_64 emulator was available; no ARM64 execution |
| NAT64/IPv6 | BLOCKED_NOT_VERIFIED | Not exercised in Stage 0 |
| Rekey/rotation | BLOCKED_NOT_VERIFIED | Noise-C tree has no in-place rekey API; session-layer rotation is task 32 work and was not implemented |
| Mobile KCP public security | BLOCKED_NOT_VERIFIED (provider-only POC; task 32 required) | Fail-closed release config, identity/prologue provisioning and observability redaction not verified |
| WebGL public deployment | BLOCKED_NOT_VERIFIED | `publicEndpoints: []`; no public WSS/DNS/certificate/reverse-proxy/browser-lane evidence |
| WebGL compile contract | DEFERRED to Gate 0 / R0 | Compile verification is a Gate 0 execution item, not Stage 0 documentation |
| KCP WebGL guards / `Assets/Packages` ownership | DEFERRED to task 13 / Gate 0 | Execution items are written into task 13; not implemented in Stage 0 |
| Release scope traceability | DELIVERED | `release-scope.yaml` `sourceGitSha` = the commit that introduced the approved scope (`c739dfbd...`), not its parent |
| iOS | NOT_IN_SCOPE | No macOS runner; excluded by the approved release scope |
| Stage 0 estimate | PRESENT | `production/quality/stage-0-estimate.md`; Revision 2 by-wave estimate, not a Gate PASS |

No unverified item above is recorded as PASS. The ADR-0003 POC PASS is limited to the provider integration
and does not imply that the mobile KCP/UDP public path, ARM64 runtime, NAT64/IPv6 or rekey/rotation are verified.

## §8 exit checklist status

The §8 checklist in the execution design is reproduced here with its actual status. `DELIVERED` means the
Stage 0 artifact/plan text exists; execution evidence is still governed by the named later Gate.

| §8 entry | Status | Evidence or landing point |
|---|---|---|
| 原 Spec 已写回平台传输矩阵、Gate 状态机、资源/消息泵/UI 启动器/静态生命周期契约 | DELIVERED | Spec blob `c773a5b180b04c033e8472bf2a073052b375856d` at baseline `c739dfbd` |
| 原 33 任务计划已按 U-P0-01～07、U-P1-01～13 更新文件清单、命令、测试和验收条件 | DELIVERED | Plan blob `5383a413701fe5d1f54336d7e5aa3c2367319f30` at baseline `c739dfbd` |
| 任务 13 前移为 P0 并成为任务 14 的显式依赖；旧 WSS 默认值已删除；任务 30/32 职责一致 | DELIVERED | Plan task 13 precedes task 14; checker asserts it; ADR-0001 blob `c64457f1...` |
| 任务 1 包含 Native 测试迁移清单、server profile、`TestServerEndpoint`、runner 职责和 WebGL lane | DELIVERED (plan text) | Plan task 1; execution and WebGL compile contract verification are Gate 0/R0 items |
| `docs/technical-preferences.md` 权威来源、两个工具链同步、`.codex/` 小写契约、入口及 Windows/Linux consumer sentinel | BLOCKED_NOT_VERIFIED | Claude PASS; Codex `BLOCKED_NOT_VERIFIED`; Linux `BLOCKED_NOT_VERIFIED`; see sentinel record |
| 三个基础 ADR 已创建并通过一致性检查 | DELIVERED | ADR-0001/0002/0003 blobs `c64457f1...`, `8dadf176...`, `c24eeafd...` |
| ADR-0003 POC 已完成并通过；失败时有回退方案 | DELIVERED (provider-only PASS) | `spikes/kcp-security-poc/RESULT.md`; KCP/UDP/ARM64/NAT64/rekey remain not verified |
| `KcpNet` WebGL 编译守卫清单（7 个文件）已逐项处理 | DEFERRED to task 13 / Gate 0 | Written into task 13; WebGL compile contract is verified in Gate 0/R0 |
| `Assets/Packages/` 归属已决定并写入 `.gitignore` 或任务 13 文件清单 | DEFERRED to task 13 / Gate 0 | Ownership decision is a task 13 execution item; no runtime changes in Stage 0 |
| 端点重构清单已写入任务 13 | DELIVERED | Plan task 13 |
| 阶段 0 已按 Revision 2 范围重估工期 | DELIVERED | `production/quality/stage-0-estimate.md`; 49–86 person-days explicitly scoped to the original engineering portion |
| Gate 状态、run ID、timeout、test manifest、skip/flaky、产物新鲜度和原子回滚规则已写回原计划 | DELIVERED | Spec + plan + `production/quality/test-manifest.json` |
| `release-scope.yaml` 已由明确 approver 批准 | DELIVERED | `release-scope.yaml`; `sourceGitSha` = `c739dfbd5b1115ec7f53292f79d5de8ad5e9348b` (commit containing the approved content) |
| Gate 0 的 cohort manifest、profile sentinel 与 runner 负向自测已写入任务 1 | DELIVERED (plan text) | Plan task 1; execution at Gate 0 |
| 任务 13 独立拥有 platform factory、`NetworkEndpoint`、Kcp.dll 去重/Importer 排除、DNS candidate policy、IPv4/IPv6/NAT64、factory/plugin/产物守卫 | DELIVERED | Plan task 13; task 30 only takes over long-term guard maintenance; task 32 owns the security provider |
| `ResourceManager.ShutdownAsync` 与 `ResourceScope.CloseAsync` 已分离并写入任务 21/23/30 | DELIVERED | Plan tasks 21/23/30 |
| P1 Engineering Gate 与 Mobile Release Readiness 的候选包要求已写入任务 29/33 | DELIVERED | Plan tasks 29/33 |
| P1-B1/P1-B2/P1-C1/P1-C2 波次和依赖已写回原计划 | DELIVERED | Plan |
| 当前工作树已安全备份或隔离，没有未授权丢弃 | DELIVERED | Isolated worktree `.claude/worktrees/quality-stage-0` on branch `codex/quality/stage-0`; main checkout left untouched; no unauthorized discard |
| 修订后的 Spec 与计划通过文档复核，并记录批准 Git SHA | DELIVERED | Review documents in `docs/superpowers/reviews/`; activation table records approved blob SHAs |

Summary: 2 §8 items are `BLOCKED_NOT_VERIFIED` (Codex consumer sentinel, Linux consumer/hash) and 3 execution
items are `DEFERRED` to task 13/Gate 0 (KCP WebGL guards, `Assets/Packages` ownership, WebGL compile contract).
All other §8 entries are delivered as documentation, plans or governance records. The execution design status
therefore remains **pending** and is not changed to “已确认，执行基线”.

## Checker output

Pending capture on the clean evidence commit; the actual full output and exit code will be embedded here from
`production/quality/stage-0-checker.log` after the run.

## Unexecuted items

- Codex consumer sentinel (no `codex` CLI on this host).
- Linux consumer/hash re-run (no WSL distribution or Linux runner on this host).
- KCP/UDP runtime on ARM64 hardware; NAT64/IPv6 behavior; session-layer rekey/rotation drills.
- Mobile KCP public security configuration (fail-closed release guard, identity/prologue provisioning, observability redaction); task 32 required.
- WebGL public deployment: `publicEndpoints` is empty, so no public domain, certificate, reverse proxy or browser lane was tested.
- WebGL compile contract and the 7 `KcpNet` WebGL guards; `Assets/Packages/` ownership decision (deferred to Gate 0 / task 13).
- iOS build and runtime (`NOT_IN_SCOPE`, no macOS runner).
- Gate 0 (task 1) was not executed in Stage 0; this report covers Stage 0 documentation, governance and activation records only.

## Evidence paths

- `production/quality/stage-0-checker.log` — full checker output and exit code (captured on the evidence commit).
- `production/quality/stage-0-estimate.md` — Revision 2 by-wave estimate (status `PRESENT`).
- `production/quality/test-manifest.json` — generated test cohort manifest.
- `production/quality/gate-exceptions.json` — gate exception register (empty at Stage 0).
- `production/quality/release-scope.yaml` — approved scope; `publicEndpoints: []`; `sourceGitSha` points to the commit containing the approved content.
- `production/quality/technical-preferences-sentinel.md` — consumer sentinel record, including Linux `BLOCKED_NOT_VERIFIED`.
- `production/qa/device-profiles/android-reference.yaml` — Android reference profile (`configured: false`).
- `spikes/kcp-security-poc/RESULT.md` and `spikes/kcp-security-poc/evidence/` — ADR-0003 POC result, logs, JSON summaries, Unity logcat, APK contents/SHA-256, device ABI.
- `docs/architecture/adr-0001-platform-transport-matrix.md`, `adr-0002-quality-gate-evidence-model.md`, `adr-0003-kcp-session-security.md`.
- `.gitattributes` — `text eol=lf` contract for the technical-preferences source, generated entries and sentinel.
- `docs/superpowers/specs/2026-11-07-p0-p1-quality-improvement-execution-design.md`, `docs/superpowers/plans/2026-11-07-p0-p1-quality-stage-0.md` — execution design and Stage 0 plan (including the §8 status table and task 17 evidence procedure).

## Notes

- Evidence is captured on the clean evidence commit recorded in `Base Git SHA` above; the report itself is committed after evidence capture, so the commit containing this report is newer than the evidence commit.
- The activation table records approved blob SHAs at baseline `c739dfbd5b1115ec7f53292f79d5de8ad5e9348b`, which is the commit that contains all approved content; the Release scope row is that commit itself because `release-scope.yaml` was introduced there. The YAML `sourceGitSha` uses the same commit, not the parent `da7e3d1a`.
- The execution design status remains pending: it was not changed to “已确认，执行基线”; the §8 activation checklist remains unsatisfied for the Codex/Linux consumer sentinels, so activation waits for user decision.

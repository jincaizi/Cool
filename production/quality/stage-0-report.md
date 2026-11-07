# Stage 0 Report

- Date: 2026-11-07
- Run ID: `stage0-20261107T055129Z-c739dfbd`
- Source branch: `codex/quality/stage-0`
- Base Git SHA: `c739dfbd5b1115ec7f53292f79d5de8ad5e9348b`
- Checker: `bash tools/check-stage0-consistency.sh` -> **PASS**, exit 0 (captured output below)
- Technical preferences source marker: `TP-SENTINEL: 05761703d9d3a2715c41bddfc6187eb1042d253e2d0b858b579d9290fb2ad8b9`
- Claude consumer sentinel: **PASS** (opened generated file and reported marker)
- Codex consumer sentinel: **BLOCKED_NOT_VERIFIED** (no `codex` CLI on this host)
- Estimate: `production/quality/stage-0-estimate.md`
- Activation: **PENDING USER DECISION** — Documentation/governance work is complete; the §8 activation checklist is pending a decision (Codex consumer sentinel not satisfied); the execution baseline is not activated and Stage 0 is not declared fully complete.

## Status detail

| Item | Status | Evidence / reason |
|---|---|---|
| Stage 0 consistency checker | PASS | Captured tail output and exit code in “Checker output” below |
| Technical preferences sync (Claude/Codex generated entries) | PASS | `.claude/docs/technical-preferences.md`, `.codex/docs/technical-preferences.md` carry the source SHA-256 above |
| Claude consumer sentinel | PASS | `production/quality/technical-preferences-sentinel.md` |
| Codex consumer sentinel | BLOCKED_NOT_VERIFIED | No `codex` CLI; Codex minimal review not executed |
| ADR-0003 POC (Noise-C provider integration) | PASS (provider only) | `spikes/kcp-security-poc/RESULT.md` |
| KCP/UDP public path | BLOCKED_NOT_VERIFIED | POC exercised UDP only on .NET loopback; the Unity Android leg used TCP framing via `adb reverse` |
| ARM64 runtime | BLOCKED_NOT_VERIFIED | APK built for `arm64-v8a`, but only an x86_64 emulator was available; no ARM64 execution |
| NAT64/IPv6 | BLOCKED_NOT_VERIFIED | Not exercised in Stage 0 |
| Rekey/rotation | BLOCKED_NOT_VERIFIED | Noise-C tree has no in-place rekey API; session-layer rotation is task 32 work and was not implemented |
| Mobile KCP public security | BLOCKED_NOT_VERIFIED | POC accepted the provider only; fail-closed release config, identity/prologue provisioning and observability redaction not verified |
| WebGL public deployment | BLOCKED_NOT_VERIFIED | `production/quality/release-scope.yaml` has `publicEndpoints: []`; no public WSS/DNS/certificate/reverse-proxy/browser-lane evidence exists |
| iOS | NOT_IN_SCOPE | No macOS runner; excluded by the approved release scope |
| Stage 0 estimate | PRESENT | `production/quality/stage-0-estimate.md`; Revision 2 by-wave estimate, original 49–86 person-days explicitly not used as the complete estimate |

No unverified item above is recorded as PASS. The ADR-0003 POC PASS is limited to the provider integration
and does not imply that the mobile KCP/UDP public path, ARM64 runtime, NAT64/IPv6 or rekey/rotation are verified.

## Checker output

Actual captured tail output and exit code from this run:

```text
$ bash tools/check-stage0-consistency.sh
ok: production/qa/device-profiles/android-reference.yaml contains: p95Algorithm:
ok: production/qa/device-profiles/android-reference.yaml contains: rawDataFormat:
ok: no legacy -buildTarget win64
ok: no .Codex/ path in Git index
ok: docs/superpowers/specs/2026-11-07-p0-p1-quality-improvement-execution-design.md excludes: 待阶段 0 填写
STAGE 0 CHECK: PASS
exit=0
```

## Unexecuted items

- Codex consumer sentinel (no `codex` CLI on this host).
- KCP/UDP runtime on ARM64 hardware; NAT64/IPv6 behavior; session-layer rekey/rotation drills.
- Mobile KCP public security configuration (fail-closed release guard, identity/prologue provisioning, observability redaction).
- WebGL public deployment: `publicEndpoints` is empty, so no public domain, certificate, reverse proxy or browser lane was tested.
- iOS build and runtime (`NOT_IN_SCOPE`, no macOS runner).
- Gate 0 (task 1) was not executed in Stage 0; this report covers Stage 0 documentation, governance and activation records only.

## Evidence paths

- `spikes/kcp-security-poc/RESULT.md` — ADR-0003 POC result and limitations.
- `spikes/kcp-security-poc/evidence/` — POC logs, JSON summaries, Unity logcat, APK contents/SHA-256, device ABI.
- `docs/architecture/adr-0001-platform-transport-matrix.md`, `adr-0002-quality-gate-evidence-model.md`, `adr-0003-kcp-session-security.md`.
- `production/quality/technical-preferences-sentinel.md` — consumer sentinel record.
- `production/quality/release-scope.yaml` — approved scope; `publicEndpoints: []`.
- `production/qa/device-profiles/android-reference.yaml` — Android reference profile.
- `production/quality/stage-0-estimate.md` — Revision 2 by-wave estimate.
- Checker output is embedded in this report (see “Checker output”); this run produced no additional binary artifacts.

## Notes

- The execution design status remains pending: it was not changed to “已确认，执行基线”, and the §8 activation checklist remains unchecked, including the consumer sentinel item.
- The activation table records Git blob SHAs at base commit `c739dfbd5b1115ec7f53292f79d5de8ad5e9348b`; it does not imply approval of the Codex consumer sentinel.

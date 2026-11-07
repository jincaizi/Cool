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

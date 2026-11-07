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

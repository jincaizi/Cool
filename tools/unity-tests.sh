#!/usr/bin/env bash
# Unity MCP test wrapper: fixes the two common mistakes —
#   * tests-run wants JSON under --input, not bare --testMode/--testClass;
#   * the CLI default timeout is 60s, which is too short for PlayMode.
#
# Usage:
#   bash tools/unity-tests.sh EditMode
#   bash tools/unity-tests.sh EditMode ProtocolShapeGuardTests
#   bash tools/unity-tests.sh PlayMode BlockStaminaTests
set -euo pipefail

MODE="${1:-EditMode}"
CLASS="${2:-}"

if [ -n "$CLASS" ]; then
  unity-mcp-cli run-tool tests-run --timeout 300000 \
    --input "{\"testMode\":\"$MODE\",\"testClass\":\"$CLASS\"}"
else
  unity-mcp-cli run-tool tests-run --timeout 300000 \
    --input "{\"testMode\":\"$MODE\"}"
fi

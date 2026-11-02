#!/usr/bin/env bash
# In-process xunit runner：本机 `dotnet test` 的 VSTest testhost 起不来
# （Win32Exception(5) Access denied，见 Server/README.md），这里用
# tools/ServerTestRunner 在单进程内执行 Server.Tests.dll。
#
# 用法：
#   bash tools/test-server-inprocess.sh
#   bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Navigation"
#   CONFIG=Release bash tools/test-server-inprocess.sh        # benchmark 用 Release
#   bash tools/test-server-inprocess.sh --assembly <path/to/Server.Tests.dll>
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${CONFIG:-Debug}"

# 测试里有若干用例写 Path.GetTempPath()（NavDataFormat/MonsterBodyRadius/...）。
# 某些沙箱/CI 只允许写工作区，把 TEMP 指到仓库内可写目录，保证干净克隆也能跑。
TEST_TMP="$ROOT/.tmp/server-tests"
mkdir -p "$TEST_TMP"
export TEMP="$TEST_TMP" TMP="$TEST_TMP" TMPDIR="$TEST_TMP"

dotnet build "$ROOT/tools/ServerTestRunner/Runner.csproj" -c "$CONFIG" --nologo -v q

exec dotnet "$ROOT/tools/ServerTestRunner/bin/$CONFIG/net8.0/Runner.dll" "$@"

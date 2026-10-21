#!/usr/bin/env bash
# Server test wrapper: keeps console output minimal so the result does not flood
# the agent context. Extra args are forwarded (e.g. --filter "FullyQualifiedName~X").
set -euo pipefail

dotnet test Server/Server.Tests/Server.Tests.csproj \
  --logger "console;verbosity=minimal" \
  "$@"

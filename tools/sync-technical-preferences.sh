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

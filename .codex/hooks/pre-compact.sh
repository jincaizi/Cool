#!/bin/bash
# Claude Code PreCompact hook: dump a compact session state before context compression.
# Token hygiene (2026-10-21): never print the full working tree. Cap every list and
# summarize untracked files by top-level directory instead of dumping thousands of
# screenshots/logs into the post-compaction context.

MAX_LIST=20

print_list() {
    local label="$1" data="$2"
    [ -z "$data" ] && return
    local count
    count=$(printf '%s\n' "$data" | grep -c .)
    echo "$label ($count):"
    printf '%s\n' "$data" | head -n "$MAX_LIST" | while read -r f; do echo "  - $f"; done
    if [ "$count" -gt "$MAX_LIST" ]; then
        echo "  ... ($((count - MAX_LIST)) more; omitted)"
    fi
}

echo "=== SESSION STATE BEFORE COMPACTION ==="
echo "Timestamp: $(date)"

# --- Active session state file ---
STATE_FILE="production/session-state/active.md"
if [ -f "$STATE_FILE" ]; then
    echo ""
    echo "## Active Session State (from $STATE_FILE)"
    STATE_LINES=$(wc -l < "$STATE_FILE" 2>/dev/null | tr -d ' ')
    if [ "$STATE_LINES" -gt 80 ] 2>/dev/null; then
        head -n 80 "$STATE_FILE"
        echo "... (truncated — $STATE_LINES total lines, showing first 80)"
    else
        cat "$STATE_FILE"
    fi
else
    echo ""
    echo "## No active session state file found"
    echo "Consider maintaining production/session-state/active.md for better recovery."
fi

# --- Files modified this session (compact summary) ---
echo ""
echo "## Files Modified (git working tree)"

CHANGED=$(git diff --name-only 2>/dev/null)
STAGED=$(git diff --staged --name-only 2>/dev/null)
UNTRACKED=$(git ls-files --others --exclude-standard 2>/dev/null)

print_list "Unstaged changes" "$CHANGED"
print_list "Staged changes" "$STAGED"

if [ -n "$UNTRACKED" ]; then
    UNTRACKED_COUNT=$(printf '%s\n' "$UNTRACKED" | grep -c .)
    echo "New untracked files: $UNTRACKED_COUNT"
    echo "  by top-level path:"
    printf '%s\n' "$UNTRACKED" | awk -F/ '{print $1"/"}' | sort | uniq -c | sort -rn | head -n 10 | sed 's/^/    /'
    echo "  first $MAX_LIST:"
    printf '%s\n' "$UNTRACKED" | head -n "$MAX_LIST" | while read -r f; do echo "  - $f"; done
    if [ "$UNTRACKED_COUNT" -gt "$MAX_LIST" ]; then
        echo "  ... ($((UNTRACKED_COUNT - MAX_LIST)) more; omitted)"
    fi
fi

if [ -z "$CHANGED" ] && [ -z "$STAGED" ] && [ -z "$UNTRACKED" ]; then
    echo "  (no uncommitted changes)"
fi

# --- Work-in-progress design docs ---
echo ""
echo "## Design Docs — Work In Progress"

WIP_FOUND=false
for f in design/gdd/*.md; do
    [ -f "$f" ] || continue
    INCOMPLETE=$(grep -n -E "TODO|WIP|PLACEHOLDER|\[TO BE|\[TBD\]" "$f" 2>/dev/null)
    if [ -n "$INCOMPLETE" ]; then
        WIP_FOUND=true
        echo "  $f:"
        echo "$INCOMPLETE" | while read -r line; do echo "    $line"; done
    fi
done

if [ "$WIP_FOUND" = false ]; then
    echo "  (no WIP markers found in design docs)"
fi

# --- Log compaction event ---
SESSION_LOG_DIR="production/session-logs"
mkdir -p "$SESSION_LOG_DIR" 2>/dev/null
echo "Context compaction occurred at $(date)." \
    >> "$SESSION_LOG_DIR/compaction-log.txt" 2>/dev/null

echo ""
echo "## Recovery Instructions"
echo "After compaction, read $STATE_FILE to recover full working context."
echo "Then read only the files listed above that are being actively worked on."
echo "=== END SESSION STATE ==="

exit 0

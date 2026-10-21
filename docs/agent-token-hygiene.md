# Agent Token Hygiene

**Date:** 2026-10-21
**Scope:** Claude Code sessions in this repository.
**Measured on:** the three most recent transcripts under
`C:\Users\jincaizi\.claude\projects\e--CodeForJob-Cool\`
(`154a70ad`, `cfa9ba8f`, `9b7373c3`).

## What the data showed

| Session | Requests | Max context | New input | Cache re-read | Output | Subagents |
|---|---:|---:|---:|---:|---:|---:|
| `154a70ad` (10-17..21, stamina + sword qi) | 387 | 775k | 0.82M | **165.2M** | 0.83M | 6.84M |
| `cfa9ba8f` (10-18) | 325 | 802k | 1.61M | **140.9M** | 0.48M | 9.13M |
| `9b7373c3` (10-17) | 292 | 645k | 0.30M | **102.6M** | 0.40M | 12.66M |

Cache re-reads were **98.5%–99.3%** of the raw token count. The dominant cost is not the
new code or the subagents; it is re-reading a 600k–800k conversation context on every
request. The other large sinks were one-off blobs that then stayed in the context:
a 287KB screenshot payload (image tokens are resolution-based, so its token cost is
smaller than the byte count suggests), a 103KB pre-compact hook dump, 65KB/52KB `Write`
results echoing whole plan files, ~50KB of repeated skill bodies, and 169–202KB of
subagent handbacks per session. The text blobs are the ones that map directly to tokens;
image byte sizes do not.

> Cache reads are billed at a lower rate than fresh input, so the raw total is not the
> bill. The ordering still holds for latency and for reduced-rate cost: keep the context
> small and stop injecting large one-off blobs.

## The levers

### 1. Keep sessions short (biggest)

- One feature per session. When the design/spec is approved, or after implementation,
  consider starting a fresh session; use the spec, plan and SDD ledger to resume.
- Run `/compact` around **200k–300k** tokens, not at 700k+.
- Do not use the 1M window as a target. The same 100 requests at 200k context cost
  roughly a third of the cache re-reads of 650k context.

### 2. Keep the pre-compact hook compact

`.claude/hooks/pre-compact.sh` used to print every unstaged/staged/untracked path. In
`154a70ad` that was **102,867 bytes** in one hook result. It now:

- caps each list at 20 entries;
- summarizes untracked files by top-level directory with counts;
- caps the session-state file at 80 lines.

Measured output after the change: **1.4KB**. `.claude/` is gitignored, so this edit is
local-only; it applies to future compactions in this checkout.

### 3. Stop full-resolution screenshots from entering the context

The transcript stored a **287,248-byte base64 payload** for one screenshot
(`hud_joystick_inworld_acceptance.png`; the PNG itself is 215KB). Image tokens are
resolution-based, not byte-based: that capture is 1095×498, so its token cost is modest.
4K/Retina captures are the ones that hurt. Use the helper before reading:

```bash
python tools/shrink_image.py production/verify/foo.png
# -> production/verify/foo-small.jpg (max side 1024 by default)
```

Tested: the 1095×498 PNG (215KB) became a 1024×465 JPEG (59KB); the pixel count only
dropped ~15%, so the image-token saving is modest at this size and much larger for
high-resolution captures. Or use the MCP screenshot tool's resolution/crop options. If a
full-resolution image is needed for a visual check, read it near the end of a phase or
right before `/compact`.

### 4. Reduce the fixed prompt/tool surface

- The project `.mcp.json` only starts the Unity MCP (`ai-game-developer`, ~40 tools);
  the codegraph/memory/VSCode MCP servers come from the global config. Disable the
  tool groups you never call in a session. For Unity work the usual set is
  `tests-run`, `assets-refresh`, `script-execute`, `screenshot-game-view`; the
  animation/animator/asset/scene/particle/component tools can be toggled off until needed.
- Keep the always-loaded `CLAUDE.md` small. The long per-system architecture text now
  lives in `docs/agent-architecture.md`; `CLAUDE.md` keeps the compact index and the
  guardrails. Read the detailed doc only when the task touches that system.
- Do not re-load a long skill body for every small task. `brainstorming`,
  `writing-plans` and `subagent-driven-development` are ~4–14KB each per injection;
  for a 1–3 task change, inline execution is usually enough.

### 5. Avoid large-file duplication in tool results

- `Read` large files with `offset`/`limit`; use `codegraph_context` / `codegraph_explore`
  for structure instead of reading whole files.
- `Write` results echo the whole file content. For large specs/plans, prefer a shell
  heredoc so the content is not repeated in the result:

  ```bash
  cat > docs/spec.md <<'EOF'
  ... content ...
  EOF
  ```

- Review diffs, not whole files. The `review-package` script already produces a
  self-contained diff file; reviewers should read that file rather than re-reading
  every changed source file.

### 6. Skill and review economy

- Batch review feedback into one round instead of repeated "review it again" turns;
  each round re-reads the growing context.
- Limit review to the changed sections/diff; do not re-read the whole spec on every pass.
- For small features, skip the full brainstorming → writing-plans → SDD chain; use a
  short design + inline execution.

### 7. Subagent economy

- The last three sessions used 6.8M / 9.1M / 12.7M subagent tokens; the sword-qi
  feature alone used ~1.3M across 9 subagents. This is separate from the main
  cache re-reads and is billed as fresh work.
- Use the cheapest competent model for mechanical tasks (copying exact code + running
  tests); reserve stronger models for integration/debugging.
- Keep task briefs scoped to one task, and have reviewers write the full report to a
  file while returning a short verdict + `file:line` findings to the parent.
- A final review can focus on cross-task seams (protocol → bridge → spawner → visual)
  instead of re-reading all product code.

### 8. Tool-output hygiene

- `git status --short` (or path-scoped); never dump the full working tree.
- Prefer the wrappers: `bash tools/test-server.sh` (minimal logger) and
  `bash tools/unity-tests.sh EditMode|PlayMode [Class]` (correct `--input` JSON + 300s timeout).
- `Grep` with `head_limit`; `Read` with ranges; use `git diff --stat` before a full diff.
- The repo now ignores `production/verify/` (thousands of screenshots/logs) and
  `.superpowers/`; keep verification artifacts there rather than tracked.

## Quick checklist for the next feature

1. Start a fresh session; keep `/compact` around 200k–300k.
2. Run `python tools/shrink_image.py` before reading any screenshot.
3. Read `docs/agent-architecture.md` only for the subsystem you touch.
4. Use `codegraph` / offset reads; do not read whole large files.
5. Write large docs with a heredoc; review diff files, not whole trees.
6. Batch review rounds; one final integration review.
7. Use inline execution for small features; fewer, cheaper subagents for large ones.
8. Keep tool output short (`--short`, minimal logger, `head_limit`).

## Verification of this pass

- `pre-compact.sh` output: **102,867 → 1,435 bytes**.
- `git status --short`: thousands of untracked screenshot lines → **18 lines**.
- `.gitignore` now covers `production/verify/` and `.superpowers/`.
- `tools/shrink_image.py` uses Pillow (installed, 12.3.0) and writes a JPEG with a
  1024px max side by default. Test: 1095×498 PNG 215KB → 1024×465 JPEG 59KB; image
  tokens scale with pixels, so the gain is modest at this size and larger for 4K captures.
- `CLAUDE.md` moved its long architecture sections to `docs/agent-architecture.md`.
- `tools/test-server.sh` (minimal logger) and `tools/unity-tests.sh` (JSON input +
  300s timeout) verified: 25/25 filtered server tests and 12/12 ProtocolShapeGuard tests.

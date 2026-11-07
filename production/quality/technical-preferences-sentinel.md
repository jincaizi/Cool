# Technical Preferences Consumer Sentinel

- Source: `docs/technical-preferences.md`
- Marker: `TP-SENTINEL: 670a084a1a9a33de91dcbb37922d4c5c67e01c04de2c7a8bebca1481618fe096`
- Claude entry: `.claude/docs/technical-preferences.md`
- Codex entry: `.codex/docs/technical-preferences.md`
- Line endings: `.gitattributes` pins the source, both generated entries and this sentinel to `text eol=lf`, so the hash is reproducible with `core.autocrlf=true`
- Claude minimal review: PASS (independent consumer session opened `.claude/docs/technical-preferences.md` and reported `source-sha256 670a084a1a9a33de91dcbb37922d4c5c67e01c04de2c7a8bebca1481618fe096`)
- Codex minimal review: PASS (`codex-cli 0.167.0`, logged in via ChatGPT; `codex exec` opened `.codex/docs/technical-preferences.md` and returned `source-sha256 670a084a1a9a33de91dcbb37922d4c5c67e01c04de2c7a8bebca1481618fe096`)
- Linux consumer/hash: BLOCKED_NOT_VERIFIED (`wsl.exe` exists but no Linux distribution is installed; `wsl.exe -l -v` prints usage/no distro; checker not re-run under Linux)
- Evidence commands: Claude consumer read the generated entry and returned the hash; Codex command was `codex exec "Read .codex/docs/technical-preferences.md ..."` and returned the same hash.
- Recorded: 2026-11-07

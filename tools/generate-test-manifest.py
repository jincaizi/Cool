#!/usr/bin/env python3
"""Generate production/quality/test-manifest.json from Unity test sources."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "production/quality/test-manifest.json"

TEST_ATTR = re.compile(r"\[(?:Test|UnityTest|TestCase)(?:\([^\]]*\))?\]")
METHOD = re.compile(r"(?:public|internal)\s+(?:async\s+)?(?:void|IEnumerator|Task)\s+(\w+)\s*\(")
CLASS = re.compile(r"\b(?:public|internal)\s+(?:sealed\s+)?class\s+(\w+)")
NAMESPACE = re.compile(r"\bnamespace\s+([\w.]+)")

def parse_file(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8", errors="replace")
    ns = NAMESPACE.search(text)
    cls = CLASS.search(text)
    if not cls:
        return []
    prefix = (ns.group(1) + "." if ns else "") + cls.group(1) + "."
    names: list[str] = []
    for m in TEST_ATTR.finditer(text):
        tail = text[m.end():m.end() + 400]
        mm = METHOD.search(tail)
        if mm:
            names.append(prefix + mm.group(1))
    return names

def profile_for(path: Path, assembly: str) -> str:
    if assembly == "GameSys.EditorTests":
        return "editmode"
    if assembly == "GameSys.PlayModeTests":
        return "generic"
    stem = path.stem
    if stem == "AoiWorldTests":
        return "native-bots"
    if stem in {"MonsterWorldTests", "HitJudgmentAlignmentTests"}:
        return "native-monsters"
    return "native-clean"

def main() -> int:
    groups: dict[str, dict[str, set[str]]] = {}
    editor = ROOT / "Assets/Scripts/Tests/Editor"
    play = ROOT / "Assets/Scripts/Tests/PlayMode"
    native = play / "NativeNetwork"

    for path in editor.rglob("*.cs"):
        for name in parse_file(path):
            groups.setdefault("GameSys.EditorTests", {}).setdefault("editmode", set()).add(name)
    native_files = {
        "AdapterConnectTests", "AoiWorldTests", "BlockMitigationE2ETests",
        "ClientRespawnWaitTests", "CombatSyncTests", "EchoSmokeTests",
        "HitJudgmentAlignmentTests", "LoginEnterWorldTests", "MonsterWorldTests",
        "MovementSyncTests", "ThreeClientCombatConsistencyTests", "TwoClientWorldTests",
    }
    for path in play.rglob("*.cs"):
        if native in path.parents:
            continue
        asm = "KcpNet.NativePlayModeTests" if path.stem in native_files else "GameSys.PlayModeTests"
        prof = profile_for(path, asm) if asm == "KcpNet.NativePlayModeTests" else "generic"
        for name in parse_file(path):
            groups.setdefault(asm, {}).setdefault(prof, set()).add(name)
    if native.exists():
        for path in native.rglob("*.cs"):
            if path.name.startswith("TestServerEndpoint") or "Sentinel" in path.name:
                continue
            prof = profile_for(path, "KcpNet.NativePlayModeTests")
            for name in parse_file(path):
                groups.setdefault("KcpNet.NativePlayModeTests", {}).setdefault(prof, set()).add(name)
    else:
        groups.setdefault("KcpNet.NativePlayModeTests", {}).setdefault("native-clean", set())

    assemblies: dict[str, dict] = {}
    for asm, profiles in sorted(groups.items()):
        cohorts: dict[str, dict] = {}
        for prof, names in sorted(profiles.items()):
            cohorts[prof] = {
                "serverArgs": [],
                "testFilter": sorted(names),
                "fixtures": sorted({n.rsplit(".", 1)[0] for n in names}),
                "timeoutSeconds": 300,
            }
        assemblies[asm] = {
            "profile": "editmode" if asm == "GameSys.EditorTests" else "playmode",
            "cohorts": cohorts,
        }

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"generatedAt": "2026-11-07", "assemblies": assemblies}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    total = sum(len(c["testFilter"]) for a in assemblies.values() for c in a["cohorts"].values())
    print(f"wrote {OUT} ({len(assemblies)} assemblies, {total} tests)")
    return 0

if __name__ == "__main__":
    sys.exit(main())

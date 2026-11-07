#!/usr/bin/env bash
# Deploy the Unity 2022.3 Android IL2CPP POC to the running emulator and run
# one Noise XX handshake + AEAD roundtrip against the .NET 8 server.
# Usage: bash run-unity-poc.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$ROOT/out"
KEYS="$ROOT/keys"
APK="$ROOT/UnityPoc/Build/KcpSecurityPoc.apk"
SERVER="$ROOT/Server/bin/Release/net8.0/KcpSecurityPoc.Server.dll"
ADB="${ADB:-/e/Unity/Editor/2022.3.25f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
PACKAGE="com.poc.kcpsecurity"

mkdir -p "$OUT"

if [ ! -f "$APK" ]; then
  echo "missing $APK; run the Unity batchmode build first" >&2
  exit 1
fi

cd "$ROOT"

# --- device readiness ------------------------------------------------------
"$ADB" start-server > "$OUT/adb-start-server.log" 2>&1 || true
"$ADB" wait-for-device
for _ in $(seq 1 120); do
  boot="$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')"
  if [ "$boot" = "1" ]; then
    break
  fi
  sleep 1
done

# --- build/ABI evidence ----------------------------------------------------
sha256sum "$APK" > "$OUT/apk-sha256.txt"
unzip -l "$APK" > "$OUT/apk-contents.txt"
"$ADB" shell getprop ro.product.cpu.abi > "$OUT/device-abi.txt"
"$ADB" shell getprop ro.build.version.sdk > "$OUT/device-sdk.txt"

# --- .NET 8 server ---------------------------------------------------------
# The emulator guest has no working UDP route to the host here, so the Unity
# leg uses TCP framing through adb reverse. Noise itself is transport-agnostic;
# the UDP transport is exercised by run-poc.sh.
dotnet "$SERVER" \
  --transport tcp --port 47120 --hostname 127.0.0.1 \
  --key-dir "$KEYS" --client-pub "$KEYS/client.pub" \
  --result "$OUT/server-unity.json" \
  --handshake-timeout-ms 180000 --idle-timeout-ms 3000 \
  > "$OUT/server-unity.log" 2>&1 &
SERVER_PID=$!

for _ in $(seq 1 100); do
  if grep -q '"event":"listening"' "$OUT/server-unity.log" 2>/dev/null; then
    break
  fi
  sleep 0.1
done

# --- deploy ----------------------------------------------------------------
"$ADB" logcat -c || true
"$ADB" reverse tcp:47120 tcp:47120 > "$OUT/adb-reverse.log" 2>&1
"$ADB" install -r "$APK" > "$OUT/adb-install.log" 2>&1
"$ADB" shell am force-stop "$PACKAGE" || true
"$ADB" shell am start -n "$PACKAGE/com.unity3d.player.UnityPlayerActivity" > "$OUT/adb-start.log" 2>&1

# --- wait for the on-device result ----------------------------------------
for _ in $(seq 1 240); do
  "$ADB" logcat -d -s Unity:V > "$OUT/unity-logcat.txt" 2>/dev/null || true
  if grep -q "KcpPoc.*RESULT" "$OUT/unity-logcat.txt"; then
    break
  fi
  sleep 0.5
done

for _ in $(seq 1 50); do
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    break
  fi
  sleep 0.1
done
kill "$SERVER_PID" 2>/dev/null || true
wait "$SERVER_PID" || true

# --- claims ----------------------------------------------------------------
FAILURES=0
check() {
  local name="$1" condition="$2"
  if [ "$condition" = "1" ]; then
    echo "PASS $name"
  else
    echo "FAIL $name"
    FAILURES=$((FAILURES + 1))
  fi
}

check "unity_il2cpp_handshake_and_roundtrip" "$(grep -q 'RESULT PASS handshake+aead_roundtrip' "$OUT/unity-logcat.txt" && echo 1 || echo 0)"
check "server_completed_handshake" "$(grep -q '"handshake_completed": true' "$OUT/server-unity.json" && echo 1 || echo 0)"
check "server_verified_unity_client_static_key" "$(grep -q '"client_static_ok": true' "$OUT/server-unity.json" && echo 1 || echo 0)"
check "server_decrypted_unity_message" "$(grep -q '"decrypt_ok": 1' "$OUT/server-unity.json" && echo 1 || echo 0)"
check "apk_contains_arm64_and_x86_64_il2cpp" "$(grep -q 'lib/arm64-v8a/libil2cpp.so' "$OUT/apk-contents.txt" && grep -q 'lib/x86_64/libil2cpp.so' "$OUT/apk-contents.txt" && echo 1 || echo 0)"
check "apk_contains_arm64_and_x86_64_libnoise" "$(grep -q 'lib/arm64-v8a/libnoise.so' "$OUT/apk-contents.txt" && grep -q 'lib/x86_64/libnoise.so' "$OUT/apk-contents.txt" && echo 1 || echo 0)"

echo
echo "evidence:"
echo "  $OUT/unity-logcat.txt"
echo "  $OUT/server-unity.json"
echo "  $OUT/apk-sha256.txt"
echo "  $OUT/apk-contents.txt"
echo "  $OUT/device-abi.txt"

if [ "$FAILURES" -ne 0 ]; then
  echo "UNITY POC RESULT: FAIL"
  exit 1
fi

echo "UNITY POC RESULT: PASS"

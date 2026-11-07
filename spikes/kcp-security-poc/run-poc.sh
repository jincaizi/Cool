#!/usr/bin/env bash
# Run the .NET 8 side of the ADR-0003 POC:
#   handshake, AEAD roundtrip, tamper rejection, replay rejection,
#   wrong-server-identity rejection.
# Usage: bash run-poc.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$ROOT/out"
KEYS="$ROOT/keys"
SERVER="$ROOT/Server/bin/Release/net8.0/KcpSecurityPoc.Server.dll"
CLIENT="$ROOT/Client/bin/Release/net8.0/KcpSecurityPoc.Client.dll"

mkdir -p "$OUT"

if [ ! -f "$ROOT/native/win-x64/noise.dll" ]; then
  echo "missing native/win-x64/noise.dll; run native/build-windows.sh first" >&2
  exit 1
fi

cd "$ROOT"
dotnet build Server/Server.csproj -c Release > "$OUT/dotnet-build.log" 2>&1
dotnet build Client/Client.csproj -c Release >> "$OUT/dotnet-build.log" 2>&1

# keys/ contains throwaway POC-only X25519 keys shared with UnityPoc/Assets/Resources/poc_keys.txt.
# Regenerate with: dotnet Server/.../KcpSecurityPoc.Server.dll --gen-keys <dir> --name server


start_server() {
  local port="$1" result="$2" log="$3" handshake_ms="$4" transport="${5:-udp}"
  dotnet "$SERVER" \
    --transport "$transport" --port "$port" --hostname 127.0.0.1 \
    --key-dir "$KEYS" --client-pub "$KEYS/client.pub" \
    --result "$result" --handshake-timeout-ms "$handshake_ms" \
    --idle-timeout-ms 2000 > "$log" 2>&1 &
  SERVER_PID=$!
  for _ in $(seq 1 100); do
    if grep -q '"event":"listening"' "$log" 2>/dev/null; then
      return 0
    fi
    sleep 0.1
  done
  echo "server on port $port did not start listening" >&2
  return 1
}

# --- wrong server identity -------------------------------------------------
start_server 47114 "$OUT/server-wrong-id.json" "$OUT/server-wrong-id.log" 5000
set +e
dotnet "$CLIENT" \
  --port 47114 --host 127.0.0.1 \
  --key-dir "$KEYS" --server-pub "$KEYS/wrong-server.pub" \
  --result "$OUT/client-wrong-id.json" --mode wrong-identity \
  > "$OUT/client-wrong-id.log" 2>&1
WRONG_EXIT=$?
set -e
wait "$SERVER_PID" || true

# --- handshake, roundtrip, tamper, replay ---------------------------------
start_server 47113 "$OUT/server-main.json" "$OUT/server-main.log" 15000
dotnet "$CLIENT" \
  --port 47113 --host 127.0.0.1 \
  --key-dir "$KEYS" --server-pub "$KEYS/server.pub" \
  --result "$OUT/client-main.json" --mode main \
  > "$OUT/client-main.log" 2>&1
MAIN_EXIT=$?
wait "$SERVER_PID" || true

# --- same provider over TCP framing (used by the Unity emulator leg) --------
start_server 47115 "$OUT/server-tcp.json" "$OUT/server-tcp.log" 15000 tcp
dotnet "$CLIENT" \
  --transport tcp --port 47115 --host 127.0.0.1 \
  --key-dir "$KEYS" --server-pub "$KEYS/server.pub" \
  --result "$OUT/client-tcp.json" --mode main \
  > "$OUT/client-tcp.log" 2>&1
TCP_EXIT=$?
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

check "handshake_and_aead_roundtrip" "$(grep -q '"roundtrip_ok": true' "$OUT/client-main.json" && grep -q '"handshake_completed": true' "$OUT/client-main.json" && echo 1 || echo 0)"
check "server_saw_one_valid_message" "$(grep -q '"decrypt_ok": 1' "$OUT/server-main.json" && echo 1 || echo 0)"
check "tamper_and_replay_rejected" "$(grep -q '"decrypt_failures": 2' "$OUT/server-main.json" && echo 1 || echo 0)"
check "wrong_server_identity_rejected" "$([ "$WRONG_EXIT" -eq 3 ] && grep -q '"server_identity_ok": false' "$OUT/client-wrong-id.json" && echo 1 || echo 0)"
check "client_main_exit_zero" "$([ "$MAIN_EXIT" -eq 0 ] && echo 1 || echo 0)"
check "tcp_handshake_and_aead_roundtrip" "$([ "$TCP_EXIT" -eq 0 ] && grep -q '"roundtrip_ok": true' "$OUT/client-tcp.json" && echo 1 || echo 0)"
check "tcp_tamper_and_replay_rejected" "$(grep -q '"decrypt_failures": 2' "$OUT/server-tcp.json" && echo 1 || echo 0)"

echo
echo "evidence:"
echo "  $OUT/server-main.json"
echo "  $OUT/client-main.json"
echo "  $OUT/server-wrong-id.json"
echo "  $OUT/client-wrong-id.json"

if [ "$FAILURES" -ne 0 ]; then
  echo "POC RESULT: FAIL"
  exit 1
fi

echo "POC RESULT: PASS"

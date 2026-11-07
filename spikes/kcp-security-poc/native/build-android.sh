#!/usr/bin/env bash
# Build libnoise.so for arm64-v8a and x86_64 with the Unity 2022.3 NDK.
# Override the NDK with UNITY_NDK / NOISE_C_SRC if needed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NOISE_C_COMMIT="cfe25410979a87391bb9ac8d4d4bef64e9f268c6"
SRC="${NOISE_C_SRC:-$ROOT/native/noise-c-src}"
NDK="${UNITY_NDK:-/e/Unity/Editor/2022.3.25f1/Editor/Data/PlaybackEngines/AndroidPlayer/NDK}"

if [ ! -d "$SRC/src" ]; then
  git clone https://github.com/rweather/noise-c.git "$SRC"
fi
git -C "$SRC" fetch --quiet origin "$NOISE_C_COMMIT" || true
git -C "$SRC" checkout --quiet "$NOISE_C_COMMIT"

TOOLCHAIN="$NDK/toolchains/llvm/prebuilt/windows-x86_64"
CLANG="$TOOLCHAIN/bin/clang.exe"
SYSROOT="$TOOLCHAIN/sysroot"

SOURCES=(
  "$SRC/src/protocol/cipherstate.c"
  "$SRC/src/protocol/dhstate.c"
  "$SRC/src/protocol/errors.c"
  "$SRC/src/protocol/handshakestate.c"
  "$SRC/src/protocol/hashstate.c"
  "$SRC/src/protocol/internal.c"
  "$SRC/src/protocol/names.c"
  "$SRC/src/protocol/patterns.c"
  "$SRC/src/protocol/rand_os.c"
  "$SRC/src/protocol/randstate.c"
  "$SRC/src/protocol/symmetricstate.c"
  "$SRC/src/protocol/util.c"
  "$SRC/src/backend/ref/cipher-aesgcm.c"
  "$SRC/src/backend/ref/cipher-chachapoly.c"
  "$SRC/src/backend/ref/dh-curve25519.c"
  "$SRC/src/backend/ref/hash-blake2s.c"
  "$SRC/src/backend/ref/hash-blake2b.c"
  "$SRC/src/backend/ref/hash-sha256.c"
  "$SRC/src/backend/ref/hash-sha512.c"
  "$SRC/src/crypto/aes/rijndael-alg-fst.c"
  "$SRC/src/crypto/ghash/ghash.c"
  "$SRC/src/crypto/sha2/sha256.c"
  "$SRC/src/crypto/sha2/sha512.c"
  "$SRC/src/crypto/blake2/blake2s.c"
  "$SRC/src/crypto/blake2/blake2b.c"
  "$SRC/src/crypto/chacha/chacha.c"
  "$SRC/src/crypto/donna/poly1305-donna.c"
  "$SRC/src/crypto/ed25519/ed25519.c"
  "$ROOT/native/stubs.c"
)

build_abi() {
  local target="$1"
  local abi="$2"
  local out="$ROOT/native/android/$abi"
  mkdir -p "$out" "$ROOT/UnityPoc/Assets/Plugins/Android/libs/$abi"
  "$CLANG" --target="$target" --sysroot="$SYSROOT" -shared -fPIC -O2 \
    -DED25519_CUSTOMRANDOM -DED25519_REFHASH \
    -I"$SRC/include" -I"$SRC/src" -I"$SRC/src/protocol" \
    "${SOURCES[@]}" -o "$out/libnoise.so"
  cp "$out/libnoise.so" "$ROOT/UnityPoc/Assets/Plugins/Android/libs/$abi/libnoise.so"
  echo "built $out/libnoise.so"
}

build_abi aarch64-linux-android21 arm64-v8a
build_abi x86_64-linux-android21 x86_64

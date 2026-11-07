#!/usr/bin/env bash
# Build native/win-x64/noise.dll from the pinned Noise-C tree.
#
# Requirements: Visual Studio 2022 C++ tools and a Windows 10/11 SDK.
# Override paths with VS_ROOT / WINDOWS_SDK_ROOT / NOISE_C_SRC if needed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NOISE_C_COMMIT="cfe25410979a87391bb9ac8d4d4bef64e9f268c6"
SRC="${NOISE_C_SRC:-$ROOT/native/noise-c-src}"
VS_ROOT="${VS_ROOT:-/c/Program Files/Microsoft Visual Studio/2022/Community}"
SDK_ROOT="${WINDOWS_SDK_ROOT:-/d/Windows Kits/10}"

if [ ! -d "$SRC/src" ]; then
  git clone https://github.com/rweather/noise-c.git "$SRC"
fi
git -C "$SRC" fetch --quiet origin "$NOISE_C_COMMIT" || true
git -C "$SRC" checkout --quiet "$NOISE_C_COMMIT"

MSVC_VER="$(ls "$VS_ROOT/VC/Tools/MSVC" | sort -V | tail -1)"
SDK_VER="$(ls "$SDK_ROOT/Include" | sort -V | tail -1)"
CL_DIR="$VS_ROOT/VC/Tools/MSVC/$MSVC_VER/bin/Hostx64/x64"

export PATH="$CL_DIR:$PATH"
export INCLUDE="$VS_ROOT/VC/Tools/MSVC/$MSVC_VER/include;$SDK_ROOT/Include/$SDK_VER/ucrt;$SDK_ROOT/Include/$SDK_VER/um;$SDK_ROOT/Include/$SDK_VER/shared;$SDK_ROOT/Include/$SDK_VER/winrt"
export LIB="$VS_ROOT/VC/Tools/MSVC/$MSVC_VER/lib/x64;$SDK_ROOT/Lib/$SDK_VER/ucrt/x64;$SDK_ROOT/Lib/$SDK_VER/um/x64"

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

BUILD="$ROOT/native/build/win"
OBJ="$BUILD/obj"
rm -rf "$OBJ"
mkdir -p "$OBJ" "$ROOT/native/win-x64"

WIN_SOURCES=()
for source in "${SOURCES[@]}"; do
  WIN_SOURCES+=("$(cygpath -w "$source")")
done
OBJ_WIN="$(cygpath -w "$OBJ")"
DEF_WIN="$(cygpath -w "$ROOT/native/noise.def")"
OUT_WIN="$(cygpath -w "$ROOT/native/win-x64/noise.dll")"
IMPLIB_WIN="$(cygpath -w "$BUILD/noise.lib")"

cl.exe /nologo /c /O2 /W3 /std:c11 \
  /D_CRT_SECURE_NO_WARNINGS /D_CRT_NONSTDC_NO_DEPRECATE /DWIN32 \
  /DED25519_CUSTOMRANDOM /DED25519_REFHASH \
  /I"$(cygpath -w "$SRC/include")" /I"$(cygpath -w "$SRC/src")" \
  /I"$(cygpath -w "$SRC/src/protocol")" \
  /Fo"$OBJ_WIN/" "${WIN_SOURCES[@]}"

OBJECTS=()
for object in "$OBJ"/*.obj; do
  OBJECTS+=("$(cygpath -w "$object")")
done

link.exe /nologo /DLL /DEF:"$DEF_WIN" /OUT:"$OUT_WIN" /IMPLIB:"$IMPLIB_WIN" \
  "${OBJECTS[@]}" advapi32.lib

echo "built $ROOT/native/win-x64/noise.dll"

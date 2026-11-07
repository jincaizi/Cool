# ADR-0003 KCP Session Security POC — RESULT

- **Date:** 2026-11-07
- **Status:** PASS (provider only)
- **Selected provider:** Noise-C (`github.com/rweather/noise-c`), commit `cfe25410979a87391bb9ac8d4d4bef64e9f268c6` (2023-12-07), MIT. Reference implementation of the Noise Protocol Framework, revision 34 pattern set; POC protocol `Noise_XX_25519_ChaChaPoly_BLAKE2s`.
- **.NET 8 server result:** PASS — .NET SDK 8.0.419, `net8.0`; UDP handshake, AEAD roundtrip, tamper rejection, replay rejection and wrong-server-identity rejection all passed.
- **Unity 2022.3 Android IL2CPP result:** PASS (provider handshake/AEAD only) — Unity 2022.3.25f1, IL2CPP, APK contains `arm64-v8a` and `x86_64`; the x86_64 emulator completed the handshake and AEAD roundtrip under IL2CPP. The emulator leg used TCP framing through `adb reverse` because the emulator guest has no working UDP route in this host environment; the provider handshake and AEAD are transport-agnostic and were exercised identically. See **Limitations / not verified** below.
- **Handshake:** PASS (provider handshake; .NET UDP and Unity IL2CPP over TCP framing)
- **AEAD roundtrip:** PASS (provider handshake scope; .NET UDP and Unity IL2CPP over TCP framing)
- **Tamper rejection:** PASS (`NOISE_ERROR_MAC_FAILURE` = `0x4504`; provider-level on .NET UDP/TCP, not re-run on the Unity leg)
- **Replay rejection:** PASS (`NOISE_ERROR_MAC_FAILURE` = `0x4504`; provider-level on .NET UDP/TCP, not re-run on the Unity leg)
- **Wrong server identity:** PASS (provider-level on .NET; client rejects the learned static key before sending application data)
- **Fallback:** None. WSS is not selected; it remains a contingency only if task 32 integration exposes a defect that invalidates this provider.

## Limitations / not verified

This POC accepts the Noise-C **provider integration only**. It does **not** verify the mobile KCP/UDP public path.

- **Unity transport:** the Unity 2022.3 Android IL2CPP leg ran on an **x86_64 emulator**. The emulator guest had no working UDP route to the host (`ping 10.0.2.2` and `ip route get 10.0.2.2` returned "Network is unreachable"; `eth0` policy rules existed but the route tables were absent), so the Unity leg used 4-byte-length-prefixed Noise messages over **TCP framing via `adb reverse`**. The KCP/UDP datagram path was not exercised on Android by this POC. The .NET 8 POC exercises UDP only on loopback.
- **ABI:** ARM64 was **built only**. The APK contains `arm64-v8a/libil2cpp.so` and `arm64-v8a/libnoise.so`, but the only installed emulator image is x86_64; no ARM64 device or ARM64 image was available, so no ARM64 runtime execution was performed.
- **Provider maintenance:** the selected provider is Noise-C commit `cfe2541` (no tagged release) and this tree has **no in-place rekey API**. Task 32 must vendor/mirror the exact commit, implement session-layer key rotation/rekey, and run the KCP/UDP path on ARM64 hardware.
- **Network paths:** IPv6/NAT64 behavior, replay-window behavior on a real KCP transport (as opposed to the loopback UDP probe), and public-candidate packet security configuration (fail-closed release guard, identity/prologue provisioning, observability redaction) were **not verified in Stage 0**.
- **Other:** iOS was not built or run. No loss/latency/reordering, throughput, or public-internet measurements were taken. Managed key material is not zeroed in the POC.

## Candidate matrix

| Candidate | Protocol version | Package / version / date | License | Maintenance | Unity IL2CPP Android | .NET 8 | Size | Verdict |
|---|---|---|---|---|---|---|---|---|
| Noise.NET (Metalnem) | Noise spec **revision 33** | `Noise.NET` 1.0.0, 2018-05-30 | MIT | Dormant since 2018; no later release | Managed netstandard1.3, but depends on native libsodium 1.0.16; that NuGet ships only win/linux/osx natives, no Android ABI | Yes (netstandard1.3 assets) | 38 KB nupkg, 51 KB `Noise.dll`, 586 KB libsodium | **REJECTED** — protocol revision 33 (not current revision 34), unmaintained since 2018, Android requires a separate libsodium build not provided by the package |
| Noise-C (rweather) | Noise **revision 34** pattern set (XX, XX_FALLBACK, HFS) | Git commit `cfe2541`, 2023-12-07 (no tagged release) | MIT | Reference implementation; last commit 2023-12-07; stable | C; cross-compiled for arm64-v8a and x86_64 with NDK r23b and consumed via P/Invoke from Unity IL2CPP — **verified on x86_64 emulator (provider handshake/AEAD only; no KCP/UDP runtime)** | Yes, via P/Invoke — **verified** | 14 MB source tree; `noise.dll` 351 KB; `libnoise.so` 224 KB (arm64) / 264 KB (x86_64) | **SELECTED (provider only)** |
| wolfSSL DTLS | DTLS 1.3 supported since the 5.7.x line; 5.8.x carried DTLS 1.3 reliability fixes | 5.8.4 (2025-11-21); actively maintained | GPLv2 or commercial | Active | Native C; cross-compiling possible, but no maintained Android binding for Unity | No maintained .NET 8 binding; P/Invoke required | Multi-MB native library per ABI | **REJECTED** — GPLv2/commercial licensing is not acceptable for this closed client without a commercial license; no maintained .NET 8 or Unity binding; DTLS record layer over reliable KCP is a poor fit |
| BouncyCastle C# DTLS | **DTLS 1.2 only** (`DtlsClientProtocol`/`DtlsServerProtocol`; no DTLS 1.3 handshake types) | `BouncyCastle.Cryptography` 2.7.0, 2026-07-30 | MIT | Active | Pure managed netstandard2.0; plausible but not verified | Yes (netstandard2.0/net6.0) | 8.73 MB nupkg; 4.89 MB netstandard2.0 DLL | **REJECTED** — protocol version condition: DTLS 1.2, not DTLS 1.3 |
| OpenSSL DTLS | **DTLS 1.3 only in OpenSSL 4.1.0 (released 2026-10-23)**; 3.2/3.5/3.6 are DTLS 1.2 only | 4.1.0, 2026-10-23 | Apache 2.0 | Active | Native; per-ABI build required; no maintained Unity binding | No maintained .NET 8 binding; P/Invoke required | Multi-MB native library per ABI | **REJECTED** — DTLS 1.3 support was two weeks old at POC date, not enough for production selection; no maintained .NET 8 binding; native packaging cost; DTLS-over-KCP mismatch |
| Torann (additional candidate evaluated) | Noise **revision 34** | `Torann` 0.3.1, 2026-10-10 | MIT | Active (2026) | Pure managed, but package ships only `lib/net9.0`; Unity 2022.3 cannot consume it without a port | No — net9.0-only assembly cannot be referenced by a net8.0 project | 98 KB nupkg + BouncyCastle 8.73 MB | **REJECTED** — TFM incompatibility with both the required `net8.0` target and Unity 2022.3; a fork/port would be self-maintained code |

Protocol version is an elimination condition. BouncyCastle was rejected solely because it implements DTLS 1.2; Noise.NET was rejected, in part, because it implements Noise revision 33 rather than the current revision 34. Neither was accepted merely because it could run.

## Selection

Noise-C is the only evaluated provider that is simultaneously:

- a maintained reference implementation of the current Noise revision (MIT),
- consumable from both .NET 8 and Unity 2022.3 IL2CPP without a native dependency the package does not supply,
- small enough for a mobile client (`libnoise.so` ≈ 0.22–0.26 MB per ABI),
- and actually buildable and runnable in this environment.

Integration points:

- **Client:** Unity IL2CPP → `DllImport("noise")` → `libnoise.so` (arm64-v8a, x86_64) in `Assets/Plugins/Android/libs/<abi>/`.
- **Server:** .NET 8 → `DllImport("noise")` → `noise.dll` next to the server assembly.
- **Identity and keys:** one static X25519 key pair per endpoint. The peer static public key is pinned out-of-band and compared with the key learned from the handshake (`NoiseSession.GetRemoteStaticKey`). The responder also verifies the initiator static key, so authentication is mutual.
- **Hostname binding:** the Noise prologue is `kcp-session-security-poc/v1|<original hostname>:<port>`; the POC never substitutes a DNS-resolved IP in the prologue. The static-key pin is the primary identity; the prologue binds the logical endpoint.
- **AEAD and nonces:** ChaCha20-Poly1305 with implicit Noise `CipherState` nonces, one counter per direction. `Noise-C` increments the nonce only after a successful decryption and rejects `2^64-1` as nonce exhaustion. There is no sliding replay window: out-of-order or replayed datagrams fail the AEAD check. KCP delivers messages in order, so this is acceptable; task 32 must confirm that the KCP layer does not redeliver a message after a successful decrypt.
- **Rekey/rotation:** this Noise-C tree exposes no in-place rekey API. Rotation is a session-layer operation: establish a new Noise session (new handshake) before the configured byte/time threshold. Task 32 implements and drills this.
- **Secret cleanup:** Noise-C clears its internal state on `noise_handshakestate_free` / `noise_cipherstate_free`. The POC's managed key arrays are not explicitly zeroed; task 32 must use zeroed buffers and clear persisted keys.
- **Observability:** logs contain protocol name, library commit, public-key fingerprints and error codes only. No private keys or plaintext application data are logged.

## .NET 8 POC

Environment: `dotnet --version` → 8.0.419 (pinned by `global.json`; SDK 10.0.105 was present but not used), target `net8.0`, shared runtime 8.0.25.

Command: `bash run-poc.sh` (builds, runs all cases, asserts results).

| Case | Result | Measured evidence |
|---|---|---|
| Provider handshake (XX, both static keys pinned) | PASS | client `handshake_ms=7.93`; server saw `client_static_ok=true`, fingerprint `b6d4a4941f575970` |
| AEAD roundtrip (37-byte payload, echo decrypted and compared) | PASS | `roundtrip_ok=true`; no unexpected response |
| Tampered ciphertext (last MAC byte flipped) | PASS | server `decrypt_rejected`, `0x4504 MAC failure` |
| Replayed ciphertext (nonce 0 resent after nonce advanced) | PASS | server `decrypt_rejected`, `0x4504 MAC failure` |
| Wrong server identity (client pins a different static key) | PASS | client exit 3; learned `99080c8fc4aa9999` vs pinned `ca5625277530ca5f`; server never completed the handshake |
| Same provider over TCP framing (used by the Unity leg) | PASS | handshake, roundtrip, tamper and replay all pass (`server-tcp.json`: `decrypt_ok=1`, `decrypt_failures=2`) |

The UDP server summary is `server-main.json`: `handshake_completed=true`, `client_static_ok=true`, `decrypt_ok=1`, `decrypt_failures=2`, both failures `0x4504`. The server exits after the idle timeout.

## Unity 2022.3 Android IL2CPP POC

Build:

- Unity 2022.3.25f1, `-buildTarget Android`, `BuildScript.BuildAndroid`
- Scripting backend: IL2CPP; architectures: ARM64 + X86_64; API compatibility .NET Standard 2.1; min SDK 22; target SDK 34
- `BuildResult=Succeeded`, `errors=0`, duration 33.7 s
- APK: `UnityPoc/Build/KcpSecurityPoc.apk`, 15,886,942 bytes, SHA256 `31cce7a27a5aaa945f0f1634b46913a28a0d7dfbd9e8f821f6a34fd6183e0522`
- APK native code: `lib/arm64-v8a/libil2cpp.so`, `lib/arm64-v8a/libnoise.so`, `lib/x86_64/libil2cpp.so`, `lib/x86_64/libnoise.so`

Runtime:

- Device: `Medium_Phone_API_36` emulator, Android API 36, ABI `x86_64`
- App: `com.poc.kcpsecurity`, IL2CPP release build, stripping enabled
- logcat: `[KcpPoc] start arch=X64 pointer_bits=64 unity=2022.3.25f1 protocol=Noise_XX_25519_ChaChaPoly_BLAKE2s`
- `server_identity_ok=True learned_fingerprint=25788ed66fb10e53 pinned_fingerprint=25788ed66fb10e53`
- `handshake_complete elapsed_ms=18.19`
- `aead_roundtrip_ok=True payload_len=43 error=none elapsed_ms=71.89`
- `RESULT PASS handshake+aead_roundtrip`
- .NET server: `handshake_completed=true`, `client_static_ok=true`, `decrypt_ok=1`, client fingerprint `b6d4a4941f575970`

Caveats specific to the Unity leg (consolidated in **Limitations / not verified**):

1. **Transport:** the emulator guest's UDP path was unavailable in this environment (`ping 10.0.2.2` and `ip route get 10.0.2.2` both returned "Network is unreachable"; `eth0` policy rules existed but the route tables were absent). The Unity leg therefore used 4-byte-length-prefixed Noise messages over TCP via `adb reverse tcp:47120 tcp:47120`. This does not change the provider handshake/AEAD being tested, but it is not KCP/UDP runtime evidence on Android. The .NET server leg exercises UDP.
2. **ABI:** the only installed emulator image is x86_64, so the executed ABI is x86_64. The APK was built for ARM64 too and contains `arm64-v8a/libil2cpp.so` and `arm64-v8a/libnoise.so`, but no ARM64 execution was possible here. The ADR already states that emulator evidence accepts the library but is not sufficient for Mobile Release Readiness; task 32 must run the KCP/UDP path on ARM64 hardware.

## Evidence paths

- `spikes/kcp-security-poc/evidence/server-main.json`, `client-main.json` — UDP handshake/roundtrip/tamper/replay
- `spikes/kcp-security-poc/evidence/server-main.log`, `client-main.log`
- `spikes/kcp-security-poc/evidence/server-wrong-id.json`, `client-wrong-id.json` — wrong server identity rejection
- `spikes/kcp-security-poc/evidence/server-tcp.json`, `client-tcp.json` — TCP framing used by the Unity leg
- `spikes/kcp-security-poc/evidence/unity-logcat.txt` — IL2CPP runtime handshake/roundtrip result
- `spikes/kcp-security-poc/evidence/server-unity.json` — .NET server view of the Unity client
- `spikes/kcp-security-poc/evidence/unity-build.log` — Unity build result, toolchain, IL2CPP
- `spikes/kcp-security-poc/evidence/apk-sha256.txt`, `apk-contents.txt`, `device-abi.txt`, `device-sdk.txt`
- `spikes/kcp-security-poc/evidence/adb-install.log`, `adb-reverse.log`, `adb-start.log`
- `spikes/kcp-security-poc/evidence/emulator-cold.log` — emulator boot/network diagnostics
- `spikes/kcp-security-poc/evidence/commands.txt` — exact commands executed
- Built artifact (not committed, gitignored): `spikes/kcp-security-poc/UnityPoc/Build/KcpSecurityPoc.apk` (SHA256 above)

## Reproduction

```bash
# native (requires VS 2022 + Windows SDK / Unity NDK)
bash spikes/kcp-security-poc/native/build-windows.sh
bash spikes/kcp-security-poc/native/build-android.sh

# .NET 8 POC (UDP + TCP framing)
bash spikes/kcp-security-poc/run-poc.sh

# Unity build (batchmode) then emulator run
#   see evidence/commands.txt for the exact Unity.exe command line
bash spikes/kcp-security-poc/run-unity-poc.sh
```

The committed POC-only key set lives in `spikes/kcp-security-poc/keys/` and is mirrored into `UnityPoc/Assets/Resources/poc_keys.txt`. These are throwaway test keys; they must never be used outside this spike.

## Residual risks

- **The mobile KCP/UDP public path is not verified by this POC.** ARM64 runtime, IPv6/NAT64, replay-window behavior on real KCP, and public-candidate packet security configuration remain open; see **Limitations / not verified**.
- Noise-C has no tagged release; the POC pins commit `cfe2541`. Task 32 should vendor or mirror the exact tree and track upstream security fixes.
- The native library is per-ABI (`arm64-v8a`, `x86_64`); iOS would need an additional build. The POC did not evaluate iOS.
- Managed key material is not zeroed in the POC; production integration must clear it.
- No in-place rekey API; rotation is re-handshake at the session layer.
- Strict monotonic nonce means no out-of-order datagram tolerance; acceptable with KCP but must be confirmed in task 32.
- ARM64 runtime and the KCP/UDP Android path were not executed in this environment; Mobile Release Readiness still requires them.
- The .NET POC uses loopback only; no loss/latency/reordering or throughput measurements were made.

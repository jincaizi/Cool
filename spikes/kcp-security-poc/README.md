# KCP Session Security POC

This spike is not part of any Unity assembly or server project. It exists only to select and verify the ADR-0003 provider.

## Candidate matrix

| Candidate | Unity IL2CPP Android/iOS | .NET 8 server | License | Maintenance | Notes |
|---|---|---|---|---|---|
| Noise.NET | to verify | to verify | to verify | to verify | pure managed candidate |
| Noise-C | to verify | to verify | to verify | to verify | native binding candidate |
| wolfSSL DTLS | to verify | to verify | to verify | to verify | commercial/native candidate |
| BouncyCastle DTLS | to verify | to verify | to verify | to verify | managed candidate |
| OpenSSL DTLS | to verify | to verify | to verify | to verify | native candidate |

## Steps

1. For each candidate, record package URL/version, license, last release date, open security issues, and Unity IL2CPP notes.
2. Reject candidates that cannot be consumed by Unity 2022.3 Android IL2CPP or .NET 8.
3. Pick one candidate and build `Server` and `Client` .NET 8 console projects under this directory.
4. Implement handshake + AEAD roundtrip + tamper/replay rejection using the selected library's official sample.
5. Build a minimal Unity 2022.3 Android IL2CPP player that uses the same library; run on an emulator and capture logcat/build SHA/ABI.
6. Write `RESULT.md` with candidate matrix, selected library/version/license, commands, outputs, and PASS/FAIL/BLOCKED status.
7. If the POC fails, update ADR-0003 with the WSS fallback and mark the mobile KCP public path `BLOCKED_NOT_VERIFIED`.

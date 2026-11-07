# ADR-0003: KCP Session Security

## Status

Accepted — 2026-11-07. Provider integration verified; KCP/UDP, ARM64 runtime, NAT64/IPv6 and rekey/rotation remain blocking in task 32 / Mobile Release Readiness. Evidence and limitations: [`spikes/kcp-security-poc/RESULT.md`](../../spikes/kcp-security-poc/RESULT.md).

## Context

KCP + Protobuf provides neither confidentiality nor peer authentication. The HMAC login ticket authenticates identity/integrity but does not encrypt the transport. Non-Development public mobile candidates therefore need a maintained DTLS 1.3 or Noise implementation integrated into both the Unity 2022.3 IL2CPP client and the .NET 8 server. Self-rolled cryptographic primitives are forbidden.

## Decision

- Selected provider: **Noise-C** (`github.com/rweather/noise-c`) at commit `cfe25410979a87391bb9ac8d4d4bef64e9f268c6` (2023-12-07), MIT, reference implementation of the Noise Protocol Framework revision 34. Evaluation matrix and rejection reasons for Noise.NET, wolfSSL DTLS, BouncyCastle DTLS, OpenSSL DTLS and Torann are in [`spikes/kcp-security-poc/RESULT.md`](../../spikes/kcp-security-poc/RESULT.md).
- Evaluation criteria: Unity 2022.3 IL2CPP Android/iOS compatibility, .NET 8 server support, license, maintenance status, package size, performance, and key-lifecycle fit. The RESULT matrix records the evidence for these columns for every candidate.
- Protocol: `Noise_XX_25519_ChaChaPoly_BLAKE2s` (X25519, ChaCha20-Poly1305, BLAKE2s). XX provides mutual static-key authentication: the initiator pins the responder static key and the responder pins the initiator static key.
- Client/server integration: Unity 2022.3 IL2CPP via P/Invoke to a per-ABI `libnoise.so` (arm64-v8a, x86_64); .NET 8 server via P/Invoke to `noise.dll`. Both are built from the pinned source with the Noise-C reference backend. iOS is portable C but was not verified in the Stage 0 POC.
- Identity and key source: one static X25519 key pair per endpoint. Peer public keys are provisioned out of band and compared with the keys learned during the handshake. Identity is the pinned static key, not a DNS-resolved IP; the Noise prologue additionally binds the original `NetworkEndpoint` hostname and port.
- AEAD/nonce/replay: implicit per-direction Noise `CipherState` nonces. Decryption advances the nonce only on success, so tampered and replayed messages fail closed with `NOISE_ERROR_MAC_FAILURE`. There is no sliding replay window: messages must arrive in order, which KCP provides. Nonce exhaustion (`2^64-1`) is rejected.
- Reconnect/rekey/rotation: this Noise-C tree exposes no in-place rekey API. Rotation is a session-layer operation: establish a new Noise session at a configured byte/time threshold. Task 32 implements and drills it.
- Downgrade rejection: the protocol name is fixed and identical on both ends; there is no negotiation path to a lower version or plaintext. A wrong peer static key or prologue fails the handshake.
- Secret cleanup: Noise-C clears internal handshake/cipher state on free. Production integration must zero managed key buffers and clear persisted keys.
- Redacted observability: logs may contain the protocol name, library version/commit, public-key fingerprints and error codes; never private keys or plaintext payloads.
- Development/loopback/controlled test networks may explicitly enable plaintext KCP; non-Development public candidates must fail closed.
- If the selected provider later fails integration, the fallback is mobile WebSocket/WSS for public candidates; the fallback must be recorded here and the mobile KCP public path remains `BLOCKED_NOT_VERIFIED`.

## POC Acceptance

- .NET 8 server and Unity 2022.3 Android IL2CPP client complete the selected provider handshake.
- One AEAD message roundtrip succeeds.
- Tampered and replayed messages are rejected.
- License, maintenance, package size, and IL2CPP compatibility evidence are recorded.
- Evidence is stored in `spikes/kcp-security-poc/RESULT.md` and referenced here.
- A physical Android device is not required for the Stage 0 POC; emulator evidence is enough to accept the library, but not enough for Mobile Release Readiness.
- Result (2026-11-07): the provider acceptance criteria passed (provider only). The Unity emulator leg used TCP framing because the guest UDP route was unavailable in the POC environment, and the executed ABI was x86_64; KCP/UDP, ARM64 runtime, NAT64/IPv6 and rekey/rotation remain unverified. See `RESULT.md` § Limitations / not verified.

## Consequences

- Task 13 implements only the provider interface, configuration fail-fast, and plaintext release guard.
- This ADR accepts the provider only. It does not verify the mobile KCP/UDP public path: the Unity leg ran on an x86_64 emulator over TCP framing, ARM64 was build-only, and IPv6/NAT64, replay-window behavior on real KCP, and public-candidate packet security configuration remain unverified in Stage 0.
- Task 32 implements the selected provider, key rotation drill, and security tests, and must run the KCP/UDP path on ARM64 hardware.
- Mobile Release Readiness cannot pass until task 32 provides ARM64 KCP/UDP runtime evidence and the unverified network paths above are covered.

## Alternatives

- All mobile traffic uses WSS: valid fallback; rejected as the primary path only if the POC succeeds.
- Plaintext KCP with HMAC tickets: rejected; HMAC does not provide confidentiality or peer authentication.
- Self-rolled encryption: rejected by the original spec and this ADR.

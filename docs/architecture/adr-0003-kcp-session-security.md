# ADR-0003: KCP Session Security

## Status

Proposed — POC required before Accepted. The mobile KCP public path is not release-ready until this ADR is Accepted and the POC evidence is attached.

## Context

KCP + Protobuf provides neither confidentiality nor peer authentication. The HMAC login ticket authenticates identity/integrity but does not encrypt the transport. Non-Development public mobile candidates therefore need a maintained DTLS 1.3 or Noise implementation integrated into both the Unity 2022.3 IL2CPP client and the .NET 8 server. Self-rolled cryptographic primitives are forbidden.

## Decision

- Select exactly one external provider after the POC. Candidate set: Noise.NET, Noise-C, wolfSSL DTLS, BouncyCastle DTLS, OpenSSL DTLS.
- Evaluation criteria: Unity 2022.3 IL2CPP Android/iOS compatibility, .NET 8 server support, license, maintenance status, package size, performance, and key-lifecycle fit.
- The ADR must record: protocol/library version, license, client/server integration points, identity and key source, bidirectional key derivation, AEAD, nonce/sequence handling, replay window, reconnect/rekey/rotation, downgrade rejection, secret cleanup, and redacted observability.
- Server identity must bind to the original `NetworkEndpoint` hostname, not a DNS-resolved IP.
- Development/loopback/controlled test networks may explicitly enable plaintext KCP; non-Development public candidates must fail closed.
- If the POC fails, the fallback is mobile WebSocket/WSS for public candidates; the fallback must be recorded here and the mobile KCP public path remains `BLOCKED_NOT_VERIFIED`.

## POC Acceptance

- .NET 8 server and Unity 2022.3 Android IL2CPP client complete the selected provider handshake.
- One AEAD message roundtrip succeeds.
- Tampered and replayed messages are rejected.
- License, maintenance, package size, and IL2CPP compatibility evidence are recorded.
- Evidence is stored in `spikes/kcp-security-poc/RESULT.md` and referenced here.
- A physical Android device is not required for the Stage 0 POC; emulator evidence is enough to accept the library, but not enough for Mobile Release Readiness.

## Consequences

- Task 13 implements only the provider interface, configuration fail-fast, and plaintext release guard.
- Task 32 implements the selected provider, key rotation drill, and security tests.
- Mobile Release Readiness cannot pass while this ADR is Proposed or the POC has failed.

## Alternatives

- All mobile traffic uses WSS: valid fallback; rejected as the primary path only if the POC succeeds.
- Plaintext KCP with HMAC tickets: rejected; HMAC does not provide confidentiality or peer authentication.
- Self-rolled encryption: rejected by the original spec and this ADR.

# SAI-000 — Project Status

Version: 3.7  
Status: Active — hardening source-qualified; Premium Privacy source/CI qualified with pre-Windows staging blockers open  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Single Source of Truth

Sentinel AI remains in the production-security hardening program established from assessment commit `1218f5d39e2e98f955179d7911b013636068d373`.

- Production branch: `main`
- Hardening branch: `security/production-hardening-1218f5d`
- Verified live hardening documentation head at start of this work: `259905c3d5b9f69b037463bc367985f2e73b6ced`
- Fully qualified hardening source/test checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- Hardening automated state: **12/12 required source/automated workflows PASS**
- Premium Privacy branch: `feature/premium-privacy-foundation`
- Premium Privacy source/test checkpoint: `75edc968d243ccda8da84ea05d687d881270f28f`
- Premium Privacy workflow run: `34734415429` — **SUCCESS**
- Premium Privacy remains isolated from hardening.
- Release posture: **NOT production ready; DO NOT MERGE TO MAIN**

## Hardening Qualification State

Hardening remains source/automated qualified and was not modified by this Premium Privacy work.

Remaining hardening work is physical/external qualification: installed broker/UAC, Authenticode/revocation/catalog, quarantine/crash/recovery, Defender/firewall, devices/drivers, startup/lifecycle, signed Store packaging, Store/GCP staging, architecture runtime, resource/stability runs, and final independent re-audit.

## Premium Privacy Source/CI Checkpoint

**SOURCE IMPLEMENTED / CI VERIFIED — STAGING AND WINDOWS RUNTIME NOT VERIFIED**

Exact source checkpoint `75edc968d243ccda8da84ea05d687d881270f28f` passed the complete `Sentinel Premium Privacy Foundation` workflow, run `34734415429`.

Passed gates:

- Explorer integration acceptance;
- encryption/Vault/Secure Delete acceptance;
- related-discovery acceptance;
- gateway/entitlement acceptance;
- native Explorer x64;
- native Explorer x86;
- native Explorer ARM64;
- Sentinel desktop build;
- Sentinel gateway build;
- unsigned x64 package build;
- packaged Explorer DLL x64 PE verification.

### Explorer Integration

Implemented native packaged `IExplorerCommand` integration with a thin `Sentinel AI` submenu containing `Inspect with Sentinel AI`, `Encrypt File`, `Add to Sentinel Vault`, and `Secure Delete`. Explorer transports only bounded one-time action intent and selected filesystem paths. It performs no cryptography, deletion, broker work, Store/GCP access, subscription validation, scanning or secret handling. Sentinel reopens/revalidates the selection and performs confirmation, entitlement and feature-specific safety checks.

### Encryption / Recovery / Vault

AES-256-GCM encrypted containers, per-file DEKs, authenticated/versioned metadata, bounded chunking, corruption/truncation rejection, exact-owned failed-output cleanup and verify-before-plaintext-removal remain implemented and CI qualified. Windows current-user protection, password protection and independent recovery-key material remain. New Vault creation persists only the protected Vault envelope, requires an independent recovery key, and never persists the plaintext VMK. Existing decrypt/recovery paths are not subscription locked.

### Secure Delete

The first destructive version is implemented as **logical exact-object removal only**. It retains the verified object handle through mutation, persists/authenticates `Prepared -> IdentityVerified -> PrimaryMutationStarted` before the irreversible handle deletion request, verifies absence of the original stable identity, isolates a replacement object at the reused path, and stops at `RelatedCleanupPending` until related-artifact work is resolved.

Each destructive authorization is short-lived and durably one-use claimed. There is no unrestricted privileged `DeletePath(string path)` or arbitrary path-delete API. Overwrite/TRIM/deallocation remains unsupported in this first version and physical-media absence remains `CANNOT PROVE` unless independently demonstrated.

### Broad Attributable Copy Discovery / Cleanup

The source provider foundation supports Sentinel provenance + exact hash, bounded exact-hash directory roots, configured File History roots, configured read-only Previous Versions roots, supplied reliable Windows Search / Recent / Jump List metadata references, and local OneDrive roots with remote state kept explicitly unverified.

File count, hash-byte, depth and duration bounds plus cancellation are enforced. The Sentinel-provenance provider now reserves the global hashed-byte budget before candidate reads. Filename/extension/timestamp/size similarity alone never creates deletion authority.

Every related filesystem deletion receives a fresh exact-target validation, fresh Secure Delete authorization, fresh Premium entitlement check and a separate journaled exact-object operation. The primary authorization is never reused. Unverified candidates are never deleted; metadata references require provider-specific targeted cleanup.

### Premium Subscription Integration

Microsoft Store remains authoritative for paid status (`sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`). The existing gateway is the server-authoritative enforcement boundary.

Implemented exact one-operation scopes:

- `privacy.encrypt`
- `privacy.vault`
- `privacy.secure-delete`
- `privacy.discovery`

Capabilities are HMAC-authenticated, Store-subject-bound, exact-scope-bound, 45 seconds, and one-time consumed. Store is re-queried at issue and final validation. Deterministic acceptance covers active, inactive, expired, revoked, unrelated product, malformed entitlement/identity, Store outage, network outage, missing server credentials, wrong scope/subject, capability tamper/expiry and replay.

Offline/backend/Store-unavailable policy blocks new Premium Privacy creation/cleanup but does not trap existing encrypted/Vault data.

## Google Cloud Staging State

**STAGING NOT VERIFIED — BLOCKER OPEN.**

Privacy capability replay state, request rate state and provider concurrency state remain process-local. This does not satisfy required multi-instance Cloud Run replay/rate/concurrency semantics. A single-instance deployment is not accepted as proof of the required property.

No authenticated Google Cloud deployment surface is available in this repository session, and the source blocker would prevent a valid multi-instance staging claim regardless. Required next correction is a shared atomic backend for replay/rate/concurrency state followed by staging-only Cloud Run validation with Secret Manager, least-privilege IAM, restart/failover, safe logging, alerting and budget controls.

## Microsoft Store Test Entitlement State

**SOURCE/DETERMINISTIC CI VERIFIED — REAL STORE TEST EVIDENCE REQUIRED.**

No production Store release was submitted. Installed StoreContext / Partner Center test evidence for active, inactive, expired, revoked, Store unavailable and network unavailable remains an external/runtime gate.

## Adversarial Source Review

Separate review recorded in `docs/privacy/SAI-PRIV-007_PreWindows_Adversarial_Review.md`.

Corrected during this work:

- Medium: durable Secure Delete authorization replay gap;
- Medium: Sentinel-provenance hashing could exceed the configured global byte budget;
- Low/build: native Explorer submenu `min` portability regression.

Open:

- Medium/staging blocker: process-local replay/rate/concurrency state cannot prove multi-instance enforcement.

No new unrestricted deletion, reparse/hardlink/protected-path bypass, replacement-object deletion, broad history deletion, fuzzy duplicate deletion authority, cloud deletion overclaim, plaintext/key handoff leakage, or physical-erasure marketing claim was accepted by the review.

## Documentation State

Authoritative privacy documents:

- `docs/privacy/SAI-PRIV-000_Explorer_Integration.md`
- `docs/privacy/SAI-PRIV-001_Encrypted_Container_Format.md`
- `docs/privacy/SAI-PRIV-002_Key_Management_and_Recovery.md`
- `docs/privacy/SAI-PRIV-003_Vault_Architecture.md`
- `docs/privacy/SAI-PRIV-004_Secure_Delete_Design.md`
- `docs/privacy/SAI-PRIV-005_Copy_Discovery_Policy.md`
- `docs/privacy/SAI-PRIV-006_Premium_Entitlement.md`
- `docs/privacy/SAI-PRIV-007_PreWindows_Adversarial_Review.md`

## Current Priority

1. Replace process-local gateway replay/rate/concurrency state with a shared atomic multi-instance design.
2. Perform staging-only GCP deployment/validation with Secret Manager/IAM/alerts/budget controls.
3. Obtain real Microsoft Store test-entitlement evidence.
4. Complete provider/runtime gaps including real File History/Previous Versions/Search/Jump List behavior and OneDrive remote semantics only where a reliable provider API is introduced.
5. Begin full installed physical Windows privacy validation only after the preceding pre-Windows gates are satisfied.

## Safety Rules

- No unrestricted privileged path-delete primitive.
- Discovery and deletion remain separate.
- Every related filesystem deletion receives independent exact-target validation and authorization.
- Fuzzy similarity never grants mutation authority.
- Do not destroy entire restore/history/search stores.
- Do not scan `pagefile.sys`, `swapfile.sys`, or `hiberfil.sys` for selected-file content.
- Do not claim SSD/NVMe physical irrecoverability from logical deletion or an overwrite request.
- Entitlement never overrides filesystem safety.
- Subscription never becomes a lock on already-owned encrypted data.
- Explorer remains a thin action-intent transport only.

## Definition of Done

Hardening remains source/automated qualified but not production ready. Premium Privacy is now source/automated qualified at the recorded checkpoint, but is **not** staging verified, Windows-runtime qualified or production qualified. Shared-state GCP staging, real Store test evidence, installed destructive/provider/media/architecture tests and later independent final review remain mandatory.

---

End of Document

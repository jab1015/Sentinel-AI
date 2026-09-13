# SAI-000 — Project Status

Version: 3.6  
Status: Active — hardening source-qualified; Premium Privacy pre-Windows implementation/qualification in progress  
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
- Premium Privacy source is intentionally isolated from hardening.
- Release posture: **NOT production ready; DO NOT MERGE TO MAIN**

## Hardening Qualification State

Hardening remains source/automated qualified. It was not modified by the Premium Privacy work documented here.

Remaining hardening work is physical/external qualification: installed broker/UAC, Authenticode/revocation/catalog, quarantine/crash/recovery, Defender/firewall, devices/drivers, startup/lifecycle, signed Store packaging, Store/GCP staging, architecture runtime, resource/stability runs, and final independent re-audit.

## Premium Privacy Current State

### Explorer Integration

**SOURCE IMPLEMENTED — CI REQUALIFICATION IN PROGRESS — INSTALLED WINDOWS VALIDATION REQUIRED**

Implemented:

- packaged native `IExplorerCommand` extension;
- x64/x86/ARM64 build targets retained;
- root `Sentinel AI` Explorer submenu;
- `Inspect with Sentinel AI`;
- `Encrypt File`;
- `Add to Sentinel Vault`;
- `Secure Delete`;
- thin shell-only boundary: no cryptography, deletion, broker, Store, Google Cloud, scanning, or secrets in Explorer;
- bounded one-time handoff containing only action intent, timestamp, and filesystem selection;
- Sentinel-side strict parsing and handle-backed selection reopen/revalidation;
- premium file actions limited to one normal file per handoff in the first destructive release.

A forged same-user handoff is not authorization. Sentinel still performs user confirmation, authoritative entitlement validation, and local feature-specific safety validation.

### Encrypted Container / Encryption Core

**SOURCE IMPLEMENTED / PREVIOUSLY CI QUALIFIED — CURRENT BRANCH REQUALIFICATION IN PROGRESS**

AES-256-GCM, per-file DEK, authenticated/versioned metadata, bounded chunking, corruption/truncation rejection, exact-owned output cleanup, and verify-before-plaintext-removal semantics remain intact.

Explorer `Encrypt File` now routes into Sentinel UI, shows the exact source/output, verifies Premium entitlement server-side immediately before creation, and creates a separate verified encrypted container. The original source is not silently deleted.

### Key Protection and Recovery

**SOURCE IMPLEMENTED — WINDOWS RUNTIME REQUIRED**

Windows current-user key protection, password protection, and independent recovery-key material remain implemented. Premium entitlement is not inserted into decryption/recovery of already-owned encrypted data.

### Sentinel Vault

**SOURCE IMPLEMENTED — CURRENT BRANCH CI/WINDOWS VALIDATION REQUIRED**

In addition to the existing VMK/per-item hierarchy, durable metadata, storage/export, and lock/unlock safety foundation, the application now has a persisted Vault envelope path that:

- never persists the plaintext VMK;
- protects a new Vault with Windows current-user DPAPI plus an independent recovery key;
- requires the user to save the recovery key before first Vault creation through Explorer;
- reopens an existing Vault with current-user protection without overwriting an unrecoverable Vault;
- gates new Vault item creation through the server-authoritative Premium entitlement flow.

Existing Vault recovery/decryption is not subscription-locked.

### Secure Delete

**EXACT-OBJECT LOGICAL REMOVAL SOURCE IMPLEMENTED — CI REQUALIFICATION IN PROGRESS — PHYSICAL WINDOWS/MEDIA VALIDATION REQUIRED**

Implemented:

- existing exact target validation/revalidation, stable volume/file identity, protected/system-critical/device/reparse/directory/hardlink rejection, storage boundary snapshot, short-lived authorization, retained mutation lease, authenticated durable journal, and recovery classifier;
- new narrow logical-removal executor bound only to the retained verified file handle;
- no generic `DeletePath`, arbitrary path delete, or command-execution API;
- journal persistence/verification through `Prepared -> IdentityVerified -> PrimaryMutationStarted` before irreversible removal;
- handle-bound Windows delete-pending request and close semantics;
- exact original identity post-removal verification;
- replacement object at the former pathname is recognized as a different object and is not treated as the deleted target;
- `PrimaryRemovalVerified -> RelatedCleanupPending`; primary execution does not auto-mark `Complete`;
- structured `VERIFIED / FAILED / UNKNOWN` primary status;
- media action remains capability-honest: no overwrite/TRIM/physical-erasure claim is made by the first destructive version;
- physical-media absence remains `CANNOT PROVE` unless independently provable.

The application Secure Delete confirmation shows selected exact file, size, storage/media facts, entitlement behavior, related discovery, logical-removal semantics, and the physical-media limitation before execution.

### Broad Attributable Copy Discovery

**SOURCE IMPLEMENTED FOUNDATION — CI REQUALIFICATION IN PROGRESS — PROVIDER/RUNTIME EXPANSION REQUIRED**

Implemented:

- exact SHA-256 bounded duplicate search in user/configured roots;
- stable Sentinel provenance + hash classification;
- configured File History root discovery;
- configured read-only Previous Versions/snapshot root discovery;
- Windows Search and Recent/Jump List metadata-reference classification when reliable provider metadata is supplied;
- OneDrive local synchronization-root discovery with remote state reported separately/unverified;
- reparse avoidance, cancellation, file/byte/depth/duration bounds, and conservative unavailable/limited provider reporting;
- hardlink/same-filesystem-object detection so a second pathname does not become independent deletion authority;
- false-positive resistance: name/extension/timestamp/size alone do not authorize deletion.

Discovery grants no delete authority.

### Related Copy Cleanup

**SOURCE IMPLEMENTED FOR FILESYSTEM CANDIDATES — WINDOWS RUNTIME REQUIRED**

A separate cleanup service now requires a fresh exact-target validation, fresh Secure Delete authorization, fresh Premium entitlement check, and separate journaled exact-object operation for every confirmed/likely filesystem candidate. The primary file authorization is never reused. Likely copies require explicit user confirmation; unverified candidates are rejected. Metadata references require a provider-specific targeted operation and are not converted into filesystem deletion.

The primary operation may be marked `Complete` only after the application explicitly resolves the related-artifact phase.

### Premium Subscription Integration

**SOURCE IMPLEMENTED — CI REQUALIFICATION IN PROGRESS — GCP STAGING BLOCKED ON SHARED STATE**

Microsoft Store remains authoritative. Paid add-on: `sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`.

Implemented scopes:

- `privacy.encrypt`
- `privacy.vault`
- `privacy.secure-delete`
- `privacy.discovery`

Capabilities are HMAC-authenticated server-side, subject-bound, exact-feature scoped, 45-second lifetime, and one-time consumed. The gateway re-queries Microsoft Store at capability issue and again at final validation immediately before the local premium action. Wrong feature, wrong subject, tamper, expiry, malformed capability, and replay fail closed.

Offline/backend/Store-unavailable policy: block new premium creation/cleanup gracefully; do not trap existing encrypted/Vault data.

See `docs/privacy/SAI-PRIV-006_Premium_Entitlement.md`.

## Google Cloud Staging State

**STAGING NOT VERIFIED.**

The current gateway exposes an explicit blocker: privacy capability replay state, existing request rate state, and provider concurrency state are process-local. That does not meet the required multi-instance replay/rate property for Cloud Run. No production credentials or Store behavior were changed and no unsupported staging-complete claim is made.

Before `STAGING VERIFIED`, implement and test shared atomic replay/rate/concurrency state, then deploy a staging-only gateway with Secret Manager, least-privilege identity, logging without secrets, alerts/budget controls, restart tests, and multi-instance adversarial validation.

## Microsoft Store Test Entitlement State

**SOURCE FLOW IMPLEMENTED / REAL STORE TEST EVIDENCE STILL REQUIRED.**

The source handles active/inactive/unavailable and revalidation semantics, but real Partner Center/Store staging evidence for active, inactive, expired, revoked, Store unavailable, and network unavailable still belongs to the Windows/staging qualification phase. No production Store release was submitted.

## CI

`.github/workflows/premium-privacy-foundation.yml` now retains all previous gates and adds explicit:

- Explorer acceptance;
- encryption/Vault/Secure Delete acceptance;
- discovery acceptance;
- entitlement/gateway acceptance;
- native Explorer x64;
- native Explorer x86;
- native Explorer ARM64;
- desktop build;
- gateway build;
- x64 package;
- packaged Explorer x64 architecture verification.

The exact-head conclusion must be recorded only after the current workflow completes successfully.

## Documentation State

Authoritative privacy documents:

- `docs/privacy/SAI-PRIV-000_Explorer_Integration.md`
- `docs/privacy/SAI-PRIV-001_Encrypted_Container_Format.md`
- `docs/privacy/SAI-PRIV-002_Key_Management_and_Recovery.md`
- `docs/privacy/SAI-PRIV-003_Vault_Architecture.md`
- `docs/privacy/SAI-PRIV-004_Secure_Delete_Design.md`
- `docs/privacy/SAI-PRIV-005_Copy_Discovery_Policy.md`
- `docs/privacy/SAI-PRIV-006_Premium_Entitlement.md`

## Current Priority

1. Obtain an exact-head green Premium Privacy workflow and fix any compile/test regression without weakening gates.
2. Complete adversarial source review of the newly destructive/privacy-gated paths and fix High/Medium findings where practical.
3. Implement shared replay/rate/concurrency state required for safe multi-instance GCP staging, then perform staging-only deployment/validation.
4. Obtain real Microsoft Store test-entitlement evidence.
5. Begin the full installed physical Windows privacy validation campaign only after the preceding pre-Windows gates are satisfied.

## Safety Rules

- No unrestricted privileged path-delete primitive.
- Discovery and deletion remain separate.
- Every related filesystem deletion receives independent exact-target validation and authorization.
- Fuzzy similarity never grants mutation authority.
- Do not destroy entire restore/history/search stores.
- Do not scan `pagefile.sys`, `swapfile.sys`, or `hiberfil.sys` for file content.
- Do not claim SSD/NVMe physical irrecoverability from logical deletion or an overwrite request.
- Entitlement never overrides filesystem safety.
- Subscription never becomes a lock on already-owned encrypted data.
- Explorer remains a thin action-intent transport only.

## Definition of Done

Hardening remains source/automated qualified but not production ready. Premium Privacy now has substantially more source implementation, including narrow destructive execution and server-authoritative gating, but is **not** production qualified. Final completion still requires exact-head CI, shared-state GCP staging, Store test evidence, installed Windows destructive/provider/media/architecture tests, independent privacy/security review, and the later full Windows validation matrix.

---

End of Document

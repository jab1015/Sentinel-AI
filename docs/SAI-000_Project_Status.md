# SAI-000 — Project Status

Version: 3.8  
Status: Active — hardening and Premium Privacy source/CI qualified; Windows physical validation phase active  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Single Source of Truth

Sentinel AI remains in the production-security hardening program established from assessment commit `1218f5d39e2e98f955179d7911b013636068d373`.

- Production branch: `main`
- Hardening branch: `security/production-hardening-1218f5d`
- Fully qualified hardening source/test checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- Hardening automated state: **12/12 required source/automated workflows PASS**
- Premium Privacy branch: `feature/premium-privacy-foundation`
- Premium Privacy source/test checkpoint: `75edc968d243ccda8da84ea05d687d881270f28f`
- Premium Privacy workflow run: `34734415429` — **SUCCESS**
- Premium Privacy live branch head after source-qualification documentation: `11da43b651e45c047539ecea5461a5bdaa689a4a` or newer documentation-only head
- Premium Privacy remains isolated from hardening.
- Release posture: **NOT production ready; DO NOT MERGE TO MAIN**

## Phase Transition

**WINDOWS PHYSICAL VALIDATION MAY BEGIN NOW.**

The source/automated gates required before installed Windows testing are satisfied for both the hardening baseline and the Premium Privacy source checkpoint. Google Cloud multi-instance shared-state validation and real Microsoft Store entitlement evidence remain mandatory external qualification gates, but they do **not** need to block local/installed Windows runtime testing. Those external workstreams should proceed in parallel with Windows validation.

This is a testing-phase transition only. It is not a production-readiness, merge, or release claim.

## Hardening Qualification State

Hardening remains source/automated qualified.

Remaining hardening work is primarily physical/external qualification:

- installed broker/UAC adversarial testing;
- Authenticode/revocation/catalog fixtures;
- quarantine/crash/recovery testing;
- Defender/firewall runtime testing;
- device/driver runtime testing;
- startup/lifecycle testing;
- signed package install/upgrade/uninstall;
- real architecture runtime qualification;
- Google Cloud / Microsoft Store external validation;
- resource/stability runs;
- final High-finding and full 29-finding independent re-audit.

## Premium Privacy Source/CI Checkpoint

**SOURCE IMPLEMENTED / CI VERIFIED — WINDOWS PHYSICAL VALIDATION ACTIVE; STAGING/STORE EXTERNAL VALIDATION OPEN**

Exact source checkpoint `75edc968d243ccda8da84ea05d687d881270f28f` passed the complete `Sentinel Premium Privacy Foundation` workflow, run `34734415429`.

Passed automated gates include:

- Explorer integration acceptance;
- encryption/Vault/Secure Delete acceptance;
- related-discovery acceptance;
- gateway/entitlement acceptance;
- native Explorer x64/x86/ARM64 builds;
- Sentinel desktop build;
- Sentinel gateway build;
- unsigned x64 package build;
- packaged Explorer DLL x64 PE verification.

### Explorer Integration

Implemented packaged native `IExplorerCommand` integration with a thin `Sentinel AI` submenu containing `Inspect with Sentinel AI`, `Encrypt File`, `Add to Sentinel Vault`, and `Secure Delete`. Explorer carries only bounded action intent and filesystem selection. It performs no cryptography, deletion, broker work, Store/GCP access, subscription validation, scanning, or secret handling. Sentinel reopens/revalidates the selection and performs confirmation, entitlement, and feature-specific safety checks.

### Encryption / Recovery / Vault

AES-256-GCM encrypted containers, per-file DEKs, authenticated/versioned metadata, bounded chunking, corruption/truncation rejection, exact-owned failed-output cleanup, and verify-before-plaintext-removal are implemented and CI qualified. Windows current-user protection, password protection, and independent recovery-key material are implemented. Vault creation persists only the protected Vault envelope, requires an independent recovery key, and never persists the plaintext VMK. Existing decrypt/recovery paths are not subscription locked.

### Secure Delete

The first destructive version is implemented as **logical exact-object removal only**. It retains the verified object handle through mutation, persists/authenticates `Prepared -> IdentityVerified -> PrimaryMutationStarted` before irreversible handle deletion, verifies absence of the original stable identity, isolates any replacement object at the reused path, and proceeds to `RelatedCleanupPending` for related-artifact handling.

Each destructive authorization is short-lived and durably one-use claimed. There is no unrestricted privileged `DeletePath(string path)` or arbitrary path-delete API. Overwrite/TRIM/deallocation remains unsupported in this first version and physical-media absence remains `CANNOT PROVE` unless independently demonstrated.

### Broad Attributable Copy Discovery / Cleanup

The source provider foundation supports Sentinel provenance + exact hash, bounded exact-hash directory roots, configured File History roots, configured read-only Previous Versions roots, supplied reliable Windows Search / Recent / Jump List metadata references, and local OneDrive roots with remote state explicitly unverified.

File-count, hash-byte, depth, duration, and cancellation bounds are enforced. Filename/extension/timestamp/size similarity alone never creates deletion authority. Every related filesystem deletion requires fresh exact-target validation, fresh Secure Delete authorization, fresh Premium entitlement validation, and a separate journaled exact-object operation. Unverified candidates are never deleted automatically.

### Premium Subscription Integration

Microsoft Store remains authoritative for paid status (`sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`). The existing gateway remains the server-authoritative enforcement boundary.

Implemented one-operation scopes:

- `privacy.encrypt`
- `privacy.vault`
- `privacy.secure-delete`
- `privacy.discovery`

Capabilities are HMAC-authenticated, Store-subject-bound, exact-scope-bound, short-lived, and one-time consumed. Deterministic acceptance covers active/inactive/expired/revoked status, unrelated product, malformed identity, Store/network outage, missing credentials, wrong scope/subject, tamper, expiry, and replay. Offline/backend/Store-unavailable behavior blocks new Premium Privacy creation/cleanup but does not trap existing encrypted/Vault data.

## Windows Physical Validation — ACTIVE

Begin installed Windows testing now on the isolated Premium Privacy branch/package and the qualified hardening baseline as appropriate.

Priority Windows privacy matrix:

1. Explorer install/upgrade/uninstall, Explorer restart, file/folder/multi-select behavior, long/Unicode paths, shell crash/app unavailable, standard-user behavior.
2. Encryption normal/wrong-password/wrong-account/recovery/corruption/truncation/large-file/disk-full/cancellation/crash/output-collision/concurrency cases.
3. Vault create/add/open/lock/unlock/session-lock/timeout/recovery/corruption/crash/concurrency/package-upgrade cases.
4. Secure Delete normal/read-only/locked/access-denied/long/Unicode/reparse/junction/symlink/hardlink/target replacement/parent replacement/protected path/UAC/cancellation/crash-at-each-journal-state/volume-loss cases.
5. Discovery on real File History, Previous Versions, Windows Search, Recent/Jump Lists, OneDrive local sync, bounded duplicate scans, false-positive resistance, and provider-unavailable behavior.
6. Entitlement behavior through installed app flows wherever Store test state can be exercised.
7. x64 first, then every architecture actually intended to ship; cross-build evidence does not replace real runtime evidence.

Record exact Windows version, package identity, architecture, user privilege, filesystem/storage type, test case, observed outcome, logs/evidence, and commit/package under test.

## Google Cloud Staging State

**STAGING NOT VERIFIED — EXTERNAL BLOCKER OPEN, RUN IN PARALLEL WITH WINDOWS TESTING.**

Privacy capability replay state, request-rate state, and provider concurrency state remain process-local. This does not satisfy required multi-instance Cloud Run replay/rate/concurrency semantics. Required work remains a shared atomic backend followed by staging-only Cloud Run validation with Secret Manager, least-privilege IAM, restart/failover, safe logging, alerting, and budget controls.

This prevents GCP staging from being marked VERIFIED, but it does not invalidate or postpone local Windows runtime testing of the implemented client/privacy boundaries.

## Microsoft Store Test Entitlement State

**SOURCE/DETERMINISTIC CI VERIFIED — REAL STORE TEST EVIDENCE REQUIRED, RUN IN PARALLEL WITH WINDOWS TESTING.**

Installed StoreContext / Partner Center evidence for active, inactive, expired, revoked, Store unavailable, and network unavailable remains required. No production Store release is authorized by this status.

## Adversarial Source Review

`docs/privacy/SAI-PRIV-007_PreWindows_Adversarial_Review.md` records the source review.

Corrected findings include:

- Medium: durable Secure Delete authorization replay gap;
- Medium: Sentinel-provenance hash-byte budget bypass;
- Low/build: native Explorer submenu portability regression.

Open external finding:

- Medium/staging: process-local replay/rate/concurrency state cannot prove multi-instance enforcement.

That open staging issue is a release/staging blocker, not a reason to defer installed Windows testing.

## Current Priority

1. **Begin and execute the installed Windows physical privacy/hardening validation matrix now.**
2. In parallel, replace process-local gateway replay/rate/concurrency state with shared atomic multi-instance state.
3. In parallel, deploy/validate staging-only GCP with Secret Manager/IAM/alerts/budget controls.
4. In parallel, obtain real Microsoft Store test-entitlement evidence.
5. Expand provider-runtime evidence for File History, Previous Versions, Search, Jump Lists, and OneDrive where reliable APIs/fixtures exist.
6. Correct any Windows/runtime defect at root cause and rerun the applicable source/CI gate after changes.
7. After Windows + external gates are complete, run signed package qualification, final 1-hour/8-hour stability, privacy/security review, High-finding re-audit, and full 29-finding re-audit.

## Safety Rules

- No unrestricted privileged path-delete primitive.
- Discovery and deletion remain separate.
- Every related filesystem deletion receives independent exact-target validation and authorization.
- Fuzzy similarity never grants mutation authority.
- Do not destroy entire restore/history/search stores.
- Do not scan `pagefile.sys`, `swapfile.sys`, or `hiberfil.sys` for selected-file content.
- Do not claim SSD/NVMe physical irrecoverability from logical deletion or overwrite requests.
- Entitlement never overrides filesystem safety.
- Subscription never becomes a lock on already-owned encrypted data.
- Explorer remains a thin action-intent transport only.

## Definition of Done

Hardening and Premium Privacy are source/automated qualified at their recorded checkpoints, and the project is now in the **Windows physical validation phase** while GCP/Store external qualification proceeds in parallel. Sentinel AI is **not production ready** until required Windows runtime, signed/package, Store, Google Cloud, architecture, adversarial, crash/failure, stability, and final independent-review evidence is complete.

---

End of Document

# SAI-005 — Product Roadmap

Version: 3.1  
Status: Active — hardening source-qualified; Premium Privacy source/CI qualified with staging blockers open  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Product Vision

Sentinel AI will be a trustworthy Windows security and system-assistance platform that continuously monitors verified evidence, explains findings in plain language, detects suspicious behavior, safely contains/remediates supported threats, preserves user control, and never claims an action succeeded without verification.

## Branch Isolation Rule

- Hardening branch: `security/production-hardening-1218f5d`
- Hardening live documentation head verified at start of this work: `259905c3d5b9f69b037463bc367985f2e73b6ced`
- Fully qualified hardening source/test checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- Premium Privacy branch: `feature/premium-privacy-foundation`
- Premium Privacy source/test checkpoint: `75edc968d243ccda8da84ea05d687d881270f28f`
- Premium Privacy workflow run: `34734415429` — **SUCCESS**

Premium Privacy remains isolated from hardening. Neither branch may be merged automatically to `main`.

## Phase A — Production Security Hardening

Status: **12/12 SOURCE/AUTOMATED WORKFLOWS PASS — PHYSICAL/EXTERNAL QUALIFICATION REMAINS**

No hardening source was modified by this Premium Privacy work. Installed/UAC, Authenticode, quarantine/recovery, Defender/firewall, devices/drivers, startup/lifecycle, signed package, real architecture, Store/GCP, stability and final adversarial re-audit remain separate qualification work.

## Phase B — Active Protection

Status: **IN DEVELOPMENT / NOT RELEASE-QUALIFIED**

Objectives remain malicious/suspicious behavior detection, honest-confidence ransomware/malware indicators, user warning/explanation, supported blocking/containment, quarantine/restore, network containment, Defender integration, and exact-target elevated remediation.

## Phase C — Ask Sentinel Trust Boundary

Status: **SOURCE IMPLEMENTED — WINDOWS RUNTIME VALIDATION REMAINS**

Final displayed answers are revalidated and cannot claim security actions without verified action evidence.

## Phase D — Cloud and Entitlement Validation

Status: **SOURCE/CI VERIFIED — GCP STAGING BLOCKED ON SHARED DISTRIBUTED STATE**

The existing Google Cloud gateway remains the server-authoritative entitlement boundary. Premium Privacy adds one-operation scopes:

- `privacy.encrypt`
- `privacy.vault`
- `privacy.secure-delete`
- `privacy.discovery`

Microsoft Store remains authoritative for paid status (`sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`). The gateway re-checks Store at capability issue and again at final validation immediately before the local premium action.

Deterministic CI covers active, inactive, expired, revoked, unrelated product, malformed Store data/identity, Store outage, network outage, missing server credential, capability tamper/expiry, wrong feature/subject and replay.

Before staging can be marked verified, shared atomic replay/rate/concurrency state must replace the current process-local privacy replay and request/provider limiting behavior. Secret Manager, least-privilege IAM, restart/outage behavior, multi-instance tests, logs without secrets, alerting and budget/spend controls must then be validated on a staging-only deployment. Real StoreContext/Partner Center test evidence also remains required.

## Phase E — Release Qualification

Status: **NOT READY FOR PRODUCTION**

Signed package, install/upgrade/uninstall, supported Windows, standard/admin/UAC, startup/background, Defender/firewall, sleep/wake/network loss, crash/failure/resource testing, architecture runtime, destructive privacy testing and final stability remain required.

## Phase F — Premium Privacy Protection

Status: **SOURCE IMPLEMENTED / CI VERIFIED — STAGING + WINDOWS VALIDATION REMAIN**

Exact source checkpoint `75edc968d243ccda8da84ea05d687d881270f28f` passed the complete Premium Privacy workflow, run `34734415429`, including Explorer, encryption/Vault/Secure Delete, discovery, entitlement, native x64/x86/ARM64, desktop, gateway, x64 package and packaged Explorer architecture verification.

### P1 — File Explorer Integration

Status: **SOURCE IMPLEMENTED / CI VERIFIED — INSTALLED WINDOWS RUNTIME PENDING**

The packaged native `IExplorerCommand` exposes a `Sentinel AI` submenu containing:

- Inspect with Sentinel AI
- Encrypt File
- Add to Sentinel Vault
- Secure Delete

Explorer remains thin. It sends bounded one-time action intent and selected filesystem paths to Sentinel. It performs no cryptography, deletion, Store/GCP access, subscription validation, scanning, secret handling or broker work. Premium file commands are one-file-at-a-time in the first destructive release.

### P2/P3 — Encrypted Container + Encryption Core

Status: **SOURCE IMPLEMENTED / CI VERIFIED — INDEPENDENT REVIEW + WINDOWS RUNTIME REMAIN**

AES-256-GCM, fresh per-file DEKs, authenticated/versioned metadata, bounded processing, corruption/truncation rejection and verify-before-plaintext-removal semantics remain. Explorer Encrypt creates a separate verified `.senc` output after server-authoritative premium validation; it does not silently delete the source.

### P4 — Key Modes and Recovery

Status: **SOURCE IMPLEMENTED / CI VERIFIED — RUNTIME/REVIEW PENDING**

Windows current-user protection, password mode and independent recovery-key material remain. Subscription does not gate decryption/recovery of already-owned encrypted data.

### P5 — Sentinel Vault

Status: **SOURCE IMPLEMENTED / CI VERIFIED FOUNDATION — WINDOWS/ADVERSARIAL REVIEW PENDING**

Vault Master Key -> per-item DEK hierarchy, durable metadata, item storage/export and lock/unlock safety remain. A persisted authenticated Vault master-key envelope is wired for app use: the plaintext VMK is never persisted; new Vault creation uses Windows current-user protection plus an independent recovery key; the user must save the recovery key before the first Explorer Vault creation; an existing unrecoverable Vault is not silently overwritten. New item creation is premium-gated while recovery/decrypt access remains available independently of subscription.

### P6 — Secure Delete

Status: **EXACT-OBJECT LOGICAL REMOVAL SOURCE IMPLEMENTED / CI VERIFIED — WINDOWS/MEDIA REVIEW PENDING**

Implemented:

1. exact target validation and stable volume/file identity;
2. protected Windows/Program Files/Sentinel/system-critical/device/reparse/directory/multiple-link rejection;
3. short-lived authorization and storage-boundary revalidation;
4. durable one-use authorization claim to reject replay across restart;
5. retained verified object handle through mutation;
6. authenticated durable operation journal;
7. `Prepared -> IdentityVerified -> PrimaryMutationStarted` persisted/read back before irreversible removal;
8. logical removal requested only on the retained handle;
9. original exact identity absence verification without confusing a replacement object for the deleted one;
10. `PrimaryRemovalVerified -> RelatedCleanupPending`, never automatic completion;
11. fail-closed recovery classification;
12. structured `VERIFIED / FAILED / UNKNOWN` primary status;
13. overwrite/TRIM/physical-media erasure disabled unless independently supported and proven.

There is still no unrestricted privileged `DeletePath(string path)` or generic path-delete API.

### P7 — Broad Attributable Copy / History Discovery

Status: **SOURCE IMPLEMENTED / CI VERIFIED FOUNDATION — PROVIDER/WINDOWS VALIDATION PENDING**

Implemented provider model includes:

- Sentinel-created artifact provenance plus exact hash;
- bounded exact-SHA-256 user/configured-root duplicates;
- configured File History roots;
- configured read-only Previous Versions roots;
- Windows Search metadata/reference inputs;
- Recent/Jump List metadata/reference inputs;
- OneDrive local sync roots with remote state reported separately.

Results are only `CONFIRMED_COPY`, `LIKELY_ATTRIBUTABLE_COPY`, `METADATA_REFERENCE_ONLY` or `UNVERIFIED_CANDIDATE`. Filename/extension/timestamp/size similarity alone never creates deletion authority. Reparse traversal is refused. Bounds cover file count, hashed bytes, depth, duration and cancellation; the Sentinel provenance provider is also constrained by the global hash-byte budget. Provider unavailability/limits are reported instead of being mislabeled “nothing found.”

### P8 — Related Copy Cleanup

Status: **SOURCE IMPLEMENTED / CI VERIFIED FOR FILESYSTEM CANDIDATES — WINDOWS/PROVIDER VALIDATION PENDING**

Every related filesystem removal requires a fresh exact-target validation, a fresh Secure Delete authorization, a fresh Premium capability and its own journaled exact-object execution. The primary authorization is never reused. Likely candidates require explicit confirmation; unverified candidates are never auto-deleted. Metadata references require provider-specific targeted cleanup and must not trigger broad history/search-store destruction.

### P9 — Premium Subscription Integration

Status: **SOURCE IMPLEMENTED / CI VERIFIED — GCP STAGING + REAL STORE TEST EVIDENCE PENDING**

The existing server-authoritative Store/gateway architecture remains the only premium authority. Feature capabilities are HMAC-authenticated, Store-subject-bound, exact-scope-bound, 45 seconds, one-time consumed and revalidated against Store immediately before the operation. Wrong feature/subject, expiry, replay, tamper and malformed capabilities fail closed.

Offline/backend/Store-unavailable behavior blocks new premium creation/cleanup without blocking recovery/decryption of user-owned encrypted data.

Authoritative design: `docs/privacy/SAI-PRIV-006_Premium_Entitlement.md`.

## Premium Privacy Safety Boundary

- no unrestricted privileged path-delete primitive;
- destructive work remains bound to retained exact-object identity;
- each destructive authorization is one-use and durably claimed;
- protected/reparse/link/race defenses remain in force after subscription validation;
- discovery is never deletion authority;
- every related filesystem candidate receives independent validation/authorization;
- fuzzy similarity never authorizes deletion;
- no whole File History, shadow-copy, Windows Search or restore-store destruction;
- no pagefile/swapfile/hiberfil content scanning;
- no claim of guaranteed SSD/NVMe physical irrecoverability from logical removal;
- subscription never becomes a lock on user-owned encrypted data;
- Explorer carries intent only, never security authority.

## Pre-Windows Adversarial Review

Recorded in `docs/privacy/SAI-PRIV-007_PreWindows_Adversarial_Review.md`.

Corrected findings:

- Medium — durable Secure Delete authorization replay gap;
- Medium — Sentinel provenance provider hash-byte budget bypass;
- Low/build — native Explorer submenu `min` portability regression.

Open blocker:

- Medium/staging — gateway privacy replay, request rate and provider concurrency state remain process-local, so multi-instance Cloud Run enforcement is not yet provable.

## Current Next Milestones

### Premium Privacy branch — before full physical Windows matrix

1. Replace process-local replay/rate/concurrency state with shared atomic multi-instance state.
2. Deploy and validate staging-only Google Cloud gateway with Secret Manager, least-privilege IAM, safe logging, restart/failover, alerts and budget controls.
3. Validate Microsoft Store test entitlement lifecycle with real StoreContext/Partner Center evidence.
4. Complete remaining provider integration/runtime qualification, including real File History/Previous Versions/Search/Jump List behavior and OneDrive remote semantics only where reliable APIs are available.
5. Begin the installed physical Windows privacy matrix after those pre-Windows gates are satisfied.

### Hardening branch

Continue the separate physical/external qualification campaign without merging Premium Privacy into it.

---

End of Document

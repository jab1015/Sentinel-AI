# SAI-005 — Product Roadmap

Version: 3.0  
Status: Active — hardening source-qualified; Premium Privacy pre-Windows qualification active  
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

Status: **SOURCE ADVANCED — STAGING BLOCKED ON SHARED DISTRIBUTED STATE**

The existing Google Cloud gateway remains the server-authoritative entitlement boundary. Premium Privacy adds one-operation scopes:

- `privacy.encrypt`
- `privacy.vault`
- `privacy.secure-delete`
- `privacy.discovery`

Microsoft Store remains authoritative for paid status (`sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`). The gateway re-checks Store at capability issue and again at final validation immediately before the local premium action.

Before staging can be marked verified, shared atomic replay/rate/concurrency state must replace the current process-local privacy replay and request/provider limiting behavior. Secret Manager, least-privilege IAM, restart/outage behavior, multi-instance tests, logs without secrets, alerting and budget/spend controls must then be validated on a staging-only deployment.

## Phase E — Release Qualification

Status: **NOT READY FOR PRODUCTION**

Signed package, install/upgrade/uninstall, supported Windows, standard/admin/UAC, startup/background, Defender/firewall, sleep/wake/network loss, crash/failure/resource testing, architecture runtime, destructive privacy testing and final stability remain required.

## Phase F — Premium Privacy Protection

Status: **SOURCE IMPLEMENTATION SUBSTANTIALLY COMPLETE — EXACT-HEAD CI/STAGING/WINDOWS VALIDATION REMAIN**

### P1 — File Explorer Integration

Status: **SOURCE IMPLEMENTED — CURRENT CI REQUALIFICATION / WINDOWS RUNTIME PENDING**

The packaged native `IExplorerCommand` now exposes a `Sentinel AI` submenu containing:

- Inspect with Sentinel AI
- Encrypt File
- Add to Sentinel Vault
- Secure Delete

Explorer remains thin. It sends bounded one-time action intent and selected filesystem paths to Sentinel. It performs no cryptography, deletion, Store/GCP access, subscription validation, scanning, secret handling or broker work. Premium file commands are one-file-at-a-time in the first destructive release.

### P2/P3 — Encrypted Container + Encryption Core

Status: **SOURCE IMPLEMENTED — CURRENT CI REQUALIFICATION / INDEPENDENT REVIEW / WINDOWS RUNTIME REMAIN**

AES-256-GCM, fresh per-file DEKs, authenticated/versioned metadata, bounded processing, corruption/truncation rejection and verify-before-plaintext-removal semantics remain. Explorer Encrypt creates a separate verified `.senc` output after server-authoritative premium validation; it does not silently delete the source.

### P4 — Key Modes and Recovery

Status: **SOURCE IMPLEMENTED — RUNTIME/REVIEW PENDING**

Windows current-user protection, password mode and independent recovery-key material remain. Subscription does not gate decryption/recovery of already-owned encrypted data.

### P5 — Sentinel Vault

Status: **SOURCE IMPLEMENTED FOUNDATION — CURRENT CI/WINDOWS/ADVERSARIAL REVIEW PENDING**

Vault Master Key -> per-item DEK hierarchy, durable metadata, item storage/export and lock/unlock safety remain. A persisted authenticated Vault master-key envelope is now wired for app use: the plaintext VMK is never persisted; new Vault creation uses Windows current-user protection plus an independent recovery key; the user must save the recovery key before the first Explorer Vault creation; an existing unrecoverable Vault is not silently overwritten. New item creation is premium-gated while recovery/decrypt access remains available independently of subscription.

### P6 — Secure Delete

Status: **EXACT-OBJECT LOGICAL REMOVAL SOURCE IMPLEMENTED — CURRENT CI/WINDOWS/MEDIA REVIEW PENDING**

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

Status: **SOURCE IMPLEMENTED FOUNDATION — CURRENT CI/PROVIDER/WINDOWS VALIDATION PENDING**

Implemented provider model includes:

- Sentinel-created artifact provenance;
- bounded exact-SHA-256 user/configured-root duplicates;
- configured File History roots;
- configured read-only Previous Versions roots;
- Windows Search metadata/reference inputs;
- Recent/Jump List metadata/reference inputs;
- OneDrive local sync roots with remote state reported separately.

Results are only `CONFIRMED_COPY`, `LIKELY_ATTRIBUTABLE_COPY`, `METADATA_REFERENCE_ONLY` or `UNVERIFIED_CANDIDATE`. Filename/extension/timestamp/size similarity alone never creates deletion authority. Reparse traversal is refused. Bounds cover file count, hashed bytes, depth, duration and cancellation; provider unavailability/limits are reported instead of being mislabeled “nothing found.”

### P8 — Related Copy Cleanup

Status: **SOURCE IMPLEMENTED FOR FILESYSTEM CANDIDATES — WINDOWS/PROVIDER VALIDATION PENDING**

Every related filesystem removal requires a fresh exact-target validation, a fresh Secure Delete authorization, a fresh Premium capability and its own journaled exact-object execution. The primary authorization is never reused. Likely candidates require explicit confirmation; unverified candidates are never auto-deleted. Metadata references require provider-specific targeted cleanup and must not trigger broad history/search-store destruction.

### P9 — Premium Subscription Integration

Status: **SOURCE IMPLEMENTED — CURRENT CI/STAGING/STORE TEST EVIDENCE PENDING**

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

## Current Next Milestones

### Premium Privacy branch — before full physical Windows matrix

1. Obtain exact-head green Premium Privacy CI and fix any regression without weakening gates.
2. Finish adversarial source review and correct remaining High/Medium findings where feasible.
3. Synchronize Secure Delete/copy-discovery/entitlement documentation with exact source behavior.
4. Implement shared multi-instance replay/rate/concurrency state for the gateway.
5. Deploy and validate staging-only Google Cloud gateway with Secret Manager/IAM/alerts/budget controls.
6. Validate Microsoft Store test entitlement lifecycle with exact evidence.
7. Only then begin the installed physical Windows privacy matrix.

### Hardening branch

Continue the separate physical/external qualification campaign without merging Premium Privacy into it.

---

End of Document

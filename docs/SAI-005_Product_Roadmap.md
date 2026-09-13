# SAI-005 — Product Roadmap

Version: 3.2  
Status: Active — source/CI qualified; Windows physical validation active with external staging work in parallel  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Product Vision

Sentinel AI will be a trustworthy Windows security and system-assistance platform that continuously monitors verified evidence, explains findings in plain language, detects suspicious behavior, safely contains/remediates supported threats, preserves user control, and never claims an action succeeded without verification.

## Branch Isolation Rule

- Hardening branch: `security/production-hardening-1218f5d`
- Fully qualified hardening source/test checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- Premium Privacy branch: `feature/premium-privacy-foundation`
- Premium Privacy source/test checkpoint: `75edc968d243ccda8da84ea05d687d881270f28f`
- Premium Privacy workflow run: `34734415429` — **SUCCESS**

Premium Privacy remains isolated from hardening. Neither branch may be merged automatically to `main`.

## Phase A — Production Security Hardening

Status: **SOURCE/AUTOMATED QUALIFIED — WINDOWS/EXTERNAL VALIDATION ACTIVE**

All 12 required source/automated hardening workflows passed at the qualified checkpoint. Remaining work is physical/external: installed UAC/broker, Authenticode, quarantine/recovery, Defender/firewall, devices/drivers, lifecycle, signed packages, real architecture runtime, Store/GCP, stability, and final adversarial re-audit.

## Phase B — Active Protection

Status: **IN DEVELOPMENT / NOT RELEASE-QUALIFIED**

Objectives remain malicious/suspicious behavior detection, honest-confidence ransomware/malware indicators, user warning/explanation, supported blocking/containment, quarantine/restore, network containment, Defender integration, and exact-target elevated remediation.

## Phase C — Ask Sentinel Trust Boundary

Status: **SOURCE IMPLEMENTED — WINDOWS RUNTIME VALIDATION ACTIVE**

Final displayed answers are revalidated and cannot claim security actions without verified action evidence.

## Phase D — Cloud and Entitlement Validation

Status: **SOURCE/CI VERIFIED — GCP/STORE EXTERNAL VALIDATION OPEN IN PARALLEL**

The Google Cloud gateway remains the server-authoritative premium entitlement boundary. Implemented Premium Privacy scopes are:

- `privacy.encrypt`
- `privacy.vault`
- `privacy.secure-delete`
- `privacy.discovery`

Microsoft Store remains authoritative for paid status (`sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`). Deterministic CI covers entitlement and capability failure modes.

Multi-instance shared replay/rate/concurrency state is still required before GCP staging can be marked VERIFIED. Secret Manager, least-privilege IAM, restart/failover, safe logging, alerting, budget controls, and real StoreContext/Partner Center lifecycle evidence remain external gates.

These external gates do **not** block installed Windows runtime testing; they proceed in parallel.

## Phase E — Release Qualification

Status: **WINDOWS PHYSICAL VALIDATION ACTIVE — NOT READY FOR PRODUCTION**

Current release work includes installed package/runtime validation, supported Windows versions, standard/admin/UAC, Explorer, encryption, Vault, Secure Delete, discovery providers, Defender/firewall, startup/background, sleep/wake/network loss, crash/failure/resource behavior, architecture runtime, Store/GCP external proof, and final stability/security review.

## Phase F — Premium Privacy Protection

Status: **SOURCE IMPLEMENTED / CI VERIFIED — WINDOWS PHYSICAL VALIDATION ACTIVE**

Exact source checkpoint `75edc968d243ccda8da84ea05d687d881270f28f` passed the complete Premium Privacy workflow, run `34734415429`.

### P1 — File Explorer Integration

Status: **SOURCE/CI VERIFIED — WINDOWS RUNTIME ACTIVE**

The packaged native `IExplorerCommand` exposes:

- Inspect with Sentinel AI
- Encrypt File
- Add to Sentinel Vault
- Secure Delete

Explorer remains thin and carries intent/selection only; Sentinel performs all entitlement, exact-object, confirmation, cryptographic, and destructive safety checks.

### P2/P3 — Encrypted Container + Encryption Core

Status: **SOURCE/CI VERIFIED — WINDOWS RUNTIME + INDEPENDENT REVIEW ACTIVE/PENDING**

AES-256-GCM, per-file DEKs, authenticated/versioned metadata, bounded processing, corruption/truncation rejection, exact-owned cleanup, and verify-before-plaintext-removal semantics are implemented.

### P4 — Key Modes and Recovery

Status: **SOURCE/CI VERIFIED — WINDOWS RUNTIME ACTIVE**

Windows current-user protection, password mode, and independent recovery-key material are implemented. Recovery/decryption of user-owned encrypted data is not subscription locked.

### P5 — Sentinel Vault

Status: **SOURCE/CI VERIFIED FOUNDATION — WINDOWS RUNTIME ACTIVE**

Vault Master Key -> per-item DEK hierarchy, durable metadata, item storage/export, lock/unlock safety, protected VMK envelope, and independent recovery-key behavior are implemented.

### P6 — Secure Delete

Status: **EXACT-OBJECT LOGICAL REMOVAL SOURCE/CI VERIFIED — WINDOWS/FILESYSTEM/MEDIA VALIDATION ACTIVE**

Implemented protections include exact target identity, protected/reparse/directory/hardlink rejection, short-lived authorization, durable one-use replay protection, retained exact-object handle through mutation, authenticated durable journal, pre-mutation state persistence, exact-object logical removal, replacement-object isolation, post-removal identity verification, related-cleanup pending state, and fail-closed recovery classification.

There is no unrestricted privileged `DeletePath(string path)` primitive. Overwrite/TRIM/deallocation remains capability-gated/unsupported in the first destructive version, and physical-media absence remains `CANNOT PROVE` unless independently demonstrated.

### P7 — Broad Attributable Copy / History Discovery

Status: **SOURCE/CI VERIFIED FOUNDATION — WINDOWS/PROVIDER VALIDATION ACTIVE**

Implemented providers/foundations include Sentinel provenance + exact hash, bounded exact-hash directory search, configured File History roots, read-only Previous Versions roots, Windows Search metadata/reference inputs, Recent/Jump List metadata/reference inputs, and OneDrive local sync roots with remote state reported separately.

Discovery is bounded and cancellation-aware. Fuzzy similarity never grants deletion authority.

### P8 — Related Copy Cleanup

Status: **SOURCE/CI VERIFIED FOR FILESYSTEM CANDIDATES — WINDOWS/PROVIDER VALIDATION ACTIVE**

Every related filesystem deletion receives fresh exact-target validation, fresh Secure Delete authorization, fresh Premium entitlement validation, and its own journaled exact-object execution. Likely candidates require explicit confirmation; unverified candidates are never auto-deleted.

### P9 — Premium Subscription Integration

Status: **SOURCE/CI VERIFIED — WINDOWS INSTALLED FLOW + GCP/STORE EXTERNAL VALIDATION ACTIVE**

Feature capabilities are HMAC-authenticated, Store-subject-bound, exact-scope-bound, short-lived, and one-time consumed. Offline/backend/Store-unavailable behavior blocks new premium creation/cleanup without blocking recovery/decryption of existing user-owned encrypted data.

## Windows Physical Validation — Current Primary Phase

Begin/continue installed Windows testing now while cloud/Store external work proceeds in parallel.

Priority matrix:

1. Explorer clean install/upgrade/uninstall/restart, context menus, single/multi-select, long/Unicode paths, standard-user behavior, shell crash/app unavailable.
2. Encryption round trips, wrong credentials, recovery, corruption/truncation, large/empty files, disk full, cancellation/crashes, output collisions, concurrency.
3. Vault lifecycle, auto/session locking, recovery, corruption, abrupt termination, concurrency, package upgrade.
4. Secure Delete ordinary/empty/large/read-only/locked/access-denied files; symlink/junction/reparse/hardlink; target/parent replacement; protected paths; UAC; cancellation; crash at every journal phase; volume/storage loss.
5. Discovery on real File History, Previous Versions, Windows Search, Recent/Jump Lists, OneDrive local sync, duplicate roots, provider unavailability, bounded/cancel behavior, and false-positive resistance.
6. Installed entitlement flows wherever Store test states are available.
7. x64 plus every architecture actually intended to ship.

Any source correction discovered by Windows testing must receive a regression test and requalification before continuing.

## External Validation — Parallel Workstream

Still required before release:

1. shared atomic multi-instance replay/rate/concurrency state;
2. GCP staging deployment/validation with Secret Manager, least-privilege IAM, failover/restart, safe logging, alerts, and budget controls;
3. real Microsoft Store test-entitlement evidence;
4. signed Store-style package provenance and lifecycle evidence.

These are production/release blockers, not prerequisites for beginning local Windows physical validation.

## Safety Boundary

- no unrestricted privileged path-delete primitive;
- destructive work remains bound to retained exact-object identity;
- every related filesystem candidate receives independent validation/authorization;
- discovery is not deletion authority;
- fuzzy similarity never authorizes deletion;
- no whole File History/shadow-copy/Search/restore-store destruction;
- no pagefile/swapfile/hiberfil content scanning;
- no unsupported SSD/NVMe physical-erasure claim;
- entitlement never overrides filesystem safety;
- subscription never locks users out of recovery/decryption of their own data;
- Explorer carries intent only.

## Current Next Milestones

1. Execute Windows physical validation now and record exact package/commit/environment evidence.
2. Fix any runtime defects at root cause, add regression coverage, and re-run CI.
3. In parallel, complete GCP shared-state + staging validation.
4. In parallel, complete real Store test entitlement lifecycle validation.
5. Complete real provider/runtime discovery validation.
6. Complete signed package and architecture runtime qualification.
7. Run final 1-hour/8-hour stability/resource tests.
8. Perform independent privacy/security review, High-finding re-audit, and full 29-finding re-audit.
9. Only then consider READY FOR FINAL INDEPENDENT REVIEW / production merge decisions.

---

End of Document

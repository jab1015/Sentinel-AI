# SAI-005 — Product Roadmap

Version: 2.8  
Status: Active — Hardening qualification + isolated Premium Privacy development  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Product Vision

Sentinel AI will be a trustworthy Windows security and system-assistance platform that continuously monitors verified evidence, explains findings in plain language, detects suspicious behavior, safely contains/remediates supported threats, preserves user control, and never claims an action succeeded without verification.

## Established Product Foundation

Complete or substantially implemented:

- WinUI 3/.NET 8 desktop application.
- Hardware/software/system monitoring.
- Defender and Firewall evidence collection.
- Ask Sentinel investigation/explanation experience.
- Activity/investigation history and diagnostics.
- Optimization/repair assistance with approval and verification concepts.
- Microsoft Store/MSIX packaging foundation.
- Privileged broker architecture.
- Quarantine architecture.
- AI gateway architecture with server-side session/tier controls in source.

## Branch Isolation Rule

Production hardening qualification and Premium Privacy development are now parallel but isolated workstreams.

- Hardening qualification branch: `security/production-hardening-1218f5d`.
- Documentation-synchronized hardening head at privacy-branch creation: `7815690dcd16eb162d582183ea4b85509bf83f61`.
- Latest production source/test hardening checkpoint inherited by that branch: `6943e9d93daa0a2f3863cb5c8d41510263c454be`.
- Premium Privacy branch: `feature/premium-privacy-foundation`.
- Premium Privacy was explicitly authorized by the Product Owner on 2026-09-12 to begin in parallel.

Premium Privacy source must not be merged into the hardening qualification branch until the hardening baseline reaches `READY FOR FINAL INDEPENDENT REVIEW` or the Product Owner explicitly authorizes earlier integration. Neither branch may be merged automatically to `main`.

## Phase A — Production Security Hardening

Status: **ACTIVE — QUALIFICATION BASELINE FROZEN EXCEPT FOR PROVEN DEFECTS**

Baseline: 29 findings (14 High, 15 Medium) from commit `1218f5d...`.

Current source/test checkpoint before documentation synchronization: `6943e9d93daa0a2f3863cb5c8d41510263c454be`.

The last fully proven broad checkpoint remains `3ef08da9226e33a222768938b3dff13373ba7f61`. Later focused gates have passed, but the exact-head Windows, subprocess-boundary, driver-repair, external-research, optimization-state, A14 cleanup, network-throughput, package, and architecture workflows must be interpreted from their commit-bound results and queued/running work must never be counted as passing.

Current accomplishments include bounded subprocess execution, quarantine recovery/cleanup hardening, package payload verification, broker package-identity binding, Authenticode improvements, final Ask Sentinel display-time claim validation, initial-monitoring startup regression protection, strict firewall-containment verification with fail-closed provider-query behavior, deterministic Defender/firewall health classification, DISM/SFC classification, redaction/history/diagnostic/event-filtering tests, AI gateway security harness coverage, bounded attributable external-research evidence, fail-closed safety-state persistence, cross-process optimization execution serialization, exact-handle temporary cleanup, and transactional service-restart recovery source implementation.

A08 firewall hardening forces terminating PowerShell errors for firewall provider queries in both desktop and broker paths. Query/provider failure becomes a nonzero subprocess failure rather than apparent rule absence; duplicate parsed evidence keys are rejected. Add/remove verification remains exact and fail-closed. Runtime Group Policy, concurrent mutation, IPv4/IPv6, UAC, and real block/unblock evidence are still required.

A09/A17 subprocess ownership is routed through the common `BoundedProcessRunner` for the audited Sentinel-owned child-process paths. The source audit permits only the two exact approved runner files and separately audits the UAC broker client. Exact-head audit and broad Windows evidence remain required before formal source closure.

A10/A11 post-install driver verification requires exact device/update identity, clean result/HRESULT/restart state, known before/after driver versions, and an actual version change before a verified repair claim.

A14 temporary cleanup uses a handle-resolved canonical temp root, exact-object handle reopening, final-path boundary verification, reparse/protected-object rejection, single-hard-link requirement, same-handle age/size checks, and delete-by-handle. A14 is source-complete and requires installed adversarial runtime validation.

A16 service restart has dependency-aware broker execution, durable pre-action recovery state, verified final service state, rollback/recovery semantics, cancellation safeguards, and fail-closed dependency discovery. A16 is source-complete and requires installed service/UAC/crash/recovery validation.

A21/A22 external-research hardening includes bounded attributable passages, stale-cache controls, HTTPS/no-redirect authority validation, bounded streaming/body/XML work, fail-closed CAB expansion policy, preflight rejection before unsupported expansion, and catalog-to-package authority pinning. CAB extraction stays disabled until expansion can be bounded before and during extraction.

A24 throughput uses per-adapter baselines and intervals, establishes baselines for new adapters, and rejects reset/backward/invalid samples. Formal source closure still waits for exact-head dedicated/full Windows gates.

A28 safety-state persistence rejects unsafe state, verifies flushed writes by re-read/compare, preserves pre-action reservation on final-save failure, and holds a cross-process lease across cooldown evaluation, reservation, execution, and final persistence.

A29 continues to declare x86, x64, and ARM64. Package verification checks actual PE machine architecture and architectures must not be removed merely to obtain a green build.

The hardening branch remains **not production ready**. Exact-head CI, installed Windows runtime, Google Cloud, Store/Partner Center, signed-package, architecture, adversarial, and stability evidence remain outstanding.

Exit criteria:

- Every High and Medium finding corrected or explicitly disabled/fail-closed where a safe capability is not ready.
- Required deterministic, adversarial, packaged-runtime, cloud, Store, and architecture-specific evidence attached to each finding.
- Full High-finding adversarial re-audit and full 29-finding final re-audit completed.
- Fresh final-commit 1-hour and 8-hour stability/resource evidence completed.

## Phase B — Active Protection

Status: **IN DEVELOPMENT / NOT YET RELEASE-QUALIFIED**

Objectives:

- Detect suspicious/malicious behavior and potentially tainted files.
- Ransomware/malware indicators with honest confidence/evidence semantics.
- User warning + explanation + safe blocking/containment where technically supported.
- Protected file quarantine with restore/permanent-delete workflows.
- Suspicious network containment with explicit unblock/release controls where supported.
- Strong Defender integration without overstating Sentinel's independent blocking coverage.
- Exact-target elevated remediation through the broker.

## Phase C — Ask Sentinel Trust Boundary

Status: **SOURCE IMPLEMENTED — RUNTIME VALIDATION REMAINS**

- Final displayed answers pass deterministic validation after response replacement/composition.
- Post-orchestrator replacements do not inherit an earlier validation state.
- Advisory/inferred prose cannot assert blocked/quarantined/repaired/Defender/firewall outcomes without verified action evidence.
- Installed/runtime response paths and final provenance UX still require validation.

## Phase D — Cloud and Entitlement Validation

Status: **BLOCKED ON EXTERNAL VALIDATION**

- Deploy/validate Google Cloud gateway configuration.
- IAM + Secret Manager + provider secret isolation.
- Store entitlement validation for paid tier.
- Replay/rate/concurrency/spend controls across multiple instances.
- Abuse, timeout, outage, restart, and secret-unavailable tests.

## Phase E — Release Qualification

Status: **PLANNED AFTER HARDENING**

Required before release sign-off:

- Store-signed/package provenance validation.
- Clean install, upgrade, uninstall.
- x64 plus every actually shipped architecture.
- Supported Windows versions.
- Standard-user/admin/UAC matrices.
- Startup/background/Explorer restart/sleep-wake/network-loss behavior.
- Defender/firewall interactions.
- Crash/recovery/disk-full/access-denied tests.
- Fresh 1-hour and 8-hour stability/resource runs on the final commit.
- Independent final security review.

## Phase F — Premium Privacy Protection

Status: **EXPLICITLY AUTHORIZED — IN DEVELOPMENT ON ISOLATED FEATURE BRANCH**

Development branch: `feature/premium-privacy-foundation`.

This work proceeds in parallel with hardening qualification but is intentionally isolated from the hardening baseline.

### P1 — File Explorer Integration

Status: **SOURCE IN PROGRESS — WINDOWS/PACKAGE VALIDATION REQUIRED**

Current source foundation:

- Packaged native `IExplorerCommand` COM DLL.
- MSIX `windows.comServer` surrogate registration.
- File Explorer context registration for selected files (`*`) and selected folders (`Directory`).
- P1 exposes only `Inspect with Sentinel AI`.
- Thin shell boundary only enumerates a bounded filesystem selection, writes a bounded one-time `inspect` handoff, and activates Sentinel.
- Shell DLL performs no encryption, deletion, scanning, cloud/network calls, entitlement validation, or broker invocation.
- Sentinel consumes strict JSON, rejects unknown/destructive commands, bounds age/size/item count, canonicalizes/revalidates filesystem paths, consumes the record once, and then surfaces the inspection request.
- Handoff data is explicitly untrusted and must never become authorization for future cryptographic or destructive operations.
- Dedicated Explorer acceptance harness and Premium Privacy Windows/package workflow have been added.

Required before P1 is SOURCE COMPLETE:

- native x86/x64/ARM64 builds pass;
- desktop build passes;
- x64 MSIX contains the correct x64 shell DLL;
- source acceptance boundary passes.

Required before P1 is TESTED:

- clean install/uninstall/upgrade;
- Explorer restart;
- context menu appearance;
- file/folder/multi-select;
- unsupported shell items;
- long/Unicode paths;
- shell extension/surrogate crash;
- app unavailable;
- standard-user runtime.

Authoritative design: `docs/privacy/SAI-PRIV-000_Explorer_Integration.md`.

### P2 — Encrypted Container Specification

Status: **DESIGN COMPLETE — INDEPENDENT REVIEW + IMPLEMENTATION PENDING**

Authoritative design: `docs/privacy/SAI-PRIV-001_Encrypted_Container_Format.md`.

Version 1 uses AES-256-GCM, fresh 256-bit per-file DEKs, unique nonce material, authenticated/versioned metadata, bounded chunked streaming, strict truncation/trailing-data rejection, and verify-before-any-plaintext-removal transactions.

### P3 — Encryption Core

Status: **NOT STARTED**

Planned `FileEncryptionService` responsibilities:

- exact source validation;
- separate encrypted output;
- bounded streaming encryption;
- flush/reopen/authenticate/verify;
- structured results;
- original always retained on encryption/verification failure.

Destructive in-place encryption is prohibited in the first version.

### P4 — Key Modes and Recovery

Status: **DESIGN COMPLETE — IMPLEMENTATION PENDING**

Authoritative design: `docs/privacy/SAI-PRIV-002_Key_Management_and_Recovery.md`.

Planned modes:

- Windows current-user protection for wrapping independent file DEKs;
- portable password mode using a maintained reviewed KDF, preferring Argon2id when supportable;
- independent high-entropy recovery key with explicit Copy/Save/Print actions and no silent cloud escrow.

### P5 — Sentinel Vault

Status: **DESIGN COMPLETE — IMPLEMENTATION PENDING**

Authoritative design: `docs/privacy/SAI-PRIV-003_Vault_Architecture.md`.

Key hierarchy remains:

`Vault Master Key -> wraps independent per-item DEKs -> each DEK encrypts one item`.

Vault requirements include explicit lock states, timeout/session lock, Windows-account/password/recovery modes, encrypted sensitive metadata where practical, crash transactions, and minimal/no plaintext temporary extraction.

### P6 — Secure Delete

Status: **DESIGN COMPLETE — DESTRUCTIVE IMPLEMENTATION NOT STARTED**

Authoritative design: `docs/privacy/SAI-PRIV-004_Secure_Delete_Design.md`.

There must never be an unrestricted privileged `DeletePath(string path)` primitive. Exact-target revalidation, stable object identity, protected-path/reparse/link/race defenses, narrow broker authorization, storage-aware semantics, durable transactions, and honest result states are mandatory before the first destructive implementation.

Privacy results distinguish **VERIFIED**, **REQUESTED**, **REMAINS**, and **CANNOT PROVE**. Sentinel must not claim application-level overwrite proves physical NAND erasure.

Normal single-file Secure Delete must not directly modify `pagefile.sys`, `swapfile.sys`, or `hiberfil.sys`, wipe unrelated restore/history sets, or modify unrelated system databases.

### P7 — Broad Attributable Copy / History Discovery

Status: **DESIGN COMPLETE — IMPLEMENTATION PENDING**

Authoritative design: `docs/privacy/SAI-PRIV-005_Copy_Discovery_Policy.md`.

Candidates remain classified as:

- CONFIRMED COPY
- LIKELY ATTRIBUTABLE COPY
- METADATA / REFERENCE ONLY
- UNVERIFIED CANDIDATE

Fuzzy filename similarity alone never authorizes deletion. Discovery and deletion remain separate operations. Reports must identify sources searched, unavailable sources, confirmed/likely/reference candidates, unresolved candidates, removals, remaining items, and limitations Sentinel cannot prove.

### P8 — Explorer Privacy Commands

Status: **NOT STARTED / GATED**

Only after P1–P7 are stable may Explorer expose:

- Encrypt File
- Add to Sentinel Vault
- Secure Delete

Explorer still only activates Sentinel. Sentinel performs entitlement, exact-object, safety, user-intent, and broker checks.

### P9 — Subscription Integration

Status: **NOT STARTED**

Existing server-authoritative entitlement architecture remains authoritative for premium access. Explorer contains no reusable entitlement secret. Entitlement approval never overrides filesystem safety checks.

### Premium Privacy Marketing Boundary

Do not market Secure Delete as `guaranteed forensically unrecoverable` unless exact platform/media evidence proves that claim. Preferred positioning remains:

- Secure Delete
- Maximum safe privacy removal
- Verified removal where Sentinel can prove it
- Storage-aware secure deletion

## Premium Privacy Validation Matrices

Encryption:

- normal encrypt/decrypt
- wrong password/account
- recovery key
- corrupt header/metadata/ciphertext/tag
- truncated/trailing data
- large/empty files
- Unicode/long paths
- read-only input
- disk full
- cancellation/crash checkpoints
- output collision/concurrency/repeated encryption
- format compatibility
- nonce uniqueness
- no key logging/password persistence

Vault:

- create/add/open
- lock/unlock
- wrong password/account
- recovery
- timeout/session lock
- abrupt termination
- corrupted/missing metadata
- package upgrade
- concurrent access
- temp plaintext cleanup/failure

Secure Delete/privacy:

- normal/empty/large files
- long/Unicode/read-only/locked/access-denied
- symlink/junction/reparse/hardlink
- target/parent replacement
- protected Windows/Program Files/Sentinel package paths
- cloud-sync folders
- HDD/SATA SSD/NVMe/BitLocker
- cancellation/crash/volume loss/disk errors
- duplicate/history/search/Jump List/cloud discovery
- user decline
- false-positive duplicate resistance

Explorer:

- clean install/uninstall/upgrade
- Explorer restart
- single/multi select
- file/folder/unsupported/protected item
- long/Unicode paths
- shell crash
- app/subscription unavailable

Subscription:

- free/active/expired/revoked
- offline/backend unavailable/Store unavailable
- tampered capability/token

## Current Next Milestones

### Hardening branch

1. Let exact-head hardening workflows complete and repair only proven defects.
2. Promote A09/A17, A21/A22, and A24 only when required exact-head gates support it.
3. Finish installed broker/UAC, Authenticode, quarantine, Defender/firewall, driver, A14, A16, A19, A24, A28 runtime matrices.
4. Execute A05 Google Cloud + Store entitlement staging.
5. Complete A29 signed package + x86/x64/ARM64 runtime qualification.
6. Run fresh 1-hour/8-hour stability, High-finding review, full 29-finding review, and new-vulnerability review.

### Premium Privacy branch

1. Get P1 dedicated Windows/package CI green without weakening assertions or dropping architectures.
2. Complete installed Explorer P1 runtime matrix.
3. Implement P3 encryption core against the frozen P2 container design with deterministic corruption/nonce/crash-oriented tests from the first source commit.
4. Implement P4 key modes/recovery after library/API review.
5. Implement P5 Vault.
6. Implement P6 exact-target Secure Delete only after design review against hardened broker/quarantine patterns.
7. Implement P7 discovery providers and false-positive resistance.
8. Add P8 Explorer privacy verbs only after underlying operations are stable.
9. Integrate P9 server-authoritative entitlement.
10. Perform independent privacy/crypto review and combined integration testing only after the hardening branch reaches the required gate.

---

End of Document

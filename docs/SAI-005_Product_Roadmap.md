# SAI-005 — Product Roadmap

Version: 2.4  
Status: Active — Production hardening roadmap  
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

## Phase A — Production Security Hardening

Status: **ACTIVE**

Baseline: 29 findings (14 High, 15 Medium) from commit `1218f5d...`.

Current source checkpoint before this documentation synchronization: `cd98180f8ecaf83272f821d2e7065d72d5953395`.

The last fully proven broad checkpoint remains `3ef08da9226e33a222768938b3dff13373ba7f61`. Later focused gates have passed, but the newest exact-head Windows, subprocess-boundary, driver-repair, external-research, optimization-state, and package-architecture workflows are currently queued and must not be treated as passing until complete.

Current accomplishments include bounded subprocess execution, quarantine recovery/cleanup hardening, package payload verification, broker package-identity binding, Authenticode improvements, final Ask Sentinel display-time claim validation, initial-monitoring startup regression protection, stricter firewall-containment verification, deterministic Defender/firewall health classification, DISM/SFC classification, redaction/history/diagnostic/event-filtering tests, AI gateway security harness coverage, bounded attributable external-research evidence, fail-closed safety-state persistence, and cross-process optimization execution serialization.

The latest A17 source audit identified eight remaining direct service-process bypasses. Those paths—boot/startup correlation, storage assessment, storage execution, Windows Update repair, power-plan execution, storage-plan analysis, service repair, and network-health DNS diagnostics—have now been moved to the common `BoundedProcessRunner`. The audit was not relaxed. Exact-head source-audit and broad Windows evidence remain required before A09/A17 can move to source-complete status.

A10/A11 adversarial re-review also found that a healthy device and no-longer-offered update were not enough to prove a driver actually changed. Post-install repair verification now additionally requires a known pre-install driver version, a known post-install version, and an actual version change. Missing or unchanged versions fail closed and cannot produce a repaired/verified claim.

A21/A22 external-research hardening now includes bounded attributable passages, stale-cache controls, HTTPS/no-redirect authority validation, fail-closed CAB expansion policy, preflight rejection before Dell catalog download while unsafe expansion is disabled, and a catalog-to-package authority rule. Dell catalog package candidates must resolve to HTTPS `downloads.dell.com` on the expected port; HTTP, foreign hosts, alternate ports, credential-bearing URLs, and non-executable targets fail closed. CAB extraction remains disabled until expansion can be bounded before and during extraction.

A28 safety-state persistence now rejects corrupt, unreadable, oversized, inconsistent, and existing all-default state; verifies complete persisted records after flushed writes; preserves the pre-action reservation after post-action save failure; and holds a cross-process exclusive execution lease across cooldown evaluation, reservation, execution, and final persistence. Installed crash/filesystem/clock/fault validation still remains.

A29 continues to declare x86, x64, and ARM64. Direct architecture builds had succeeded while x86/ARM64 package payload PE verification failed. Desktop and broker project files now map package `Platform` to the corresponding .NET runtime identifier when packaging omits it. Architecture verification remains intact; the queued package jobs must prove whether additional WAP propagation work is needed. Cross-build or package success does not replace real architecture runtime qualification.

A14 temporary cleanup and A16 automatic service restart remain intentionally fail-closed/disabled until their full destructive-operation safety contracts can be implemented and validated.

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

- Final displayed answers now pass deterministic validation after response replacement/composition.
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
- Standard/admin/UAC matrices.
- Startup/background/Explorer restart/sleep-wake/network-loss behavior.
- Defender/firewall interactions.
- Crash/recovery/disk-full/access-denied tests.
- Fresh 1-hour and 8-hour stability/resource runs on the final commit.
- Independent final security review.

## Phase F — Premium Privacy Protection

Status: **PLANNED — DO NOT IMPLEMENT UNTIL CURRENT HARDENING/RELEASE GATES ARE CLOSED OR EXPLICITLY AUTHORIZED**

This phase is intentionally preserved during production hardening.

Subscription-only roadmap:

- File Explorer context-menu integration: Inspect with Sentinel AI, Secure Delete, Encrypt File, Add to Sentinel Vault.
- A thin, non-privileged Explorer extension that only identifies selected shell items and activates Sentinel; it must not directly encrypt, delete, call cloud services, validate subscriptions, scan, or invoke privileged broker actions.
- Secure Delete for explicitly selected user files using supported Windows/storage mechanisms, exact target identity and immediate revalidation, canonicalization, protected-location checks, reparse/junction/link defenses, object-race resistance, media-aware behavior, explicit confirmation, transactional/fail-closed behavior, post-operation verification, and structured evidence.
- Media-aware treatment of HDD, SATA SSD, NVMe/flash-backed storage, NTFS, BitLocker, and cloud-synchronized locations without claiming physical-media certainty Windows/firmware cannot prove.
- Privacy results must distinguish **VERIFIED**, **REQUESTED**, **REMAINS**, and **CANNOT PROVE**.
- Attributable-copy/history discovery is separate from deletion. Potential sources include Sentinel-created artifacts, File History/Previous Versions indicators, application recovery/temp copies where attribution is reliable, Windows Search/Recent/Jump List references, and cloud-sync indicators.
- Broader recovery/history operations that affect unrelated information must be reported, not silently executed as part of one-file deletion.
- Normal single-file Secure Delete must not directly modify `pagefile.sys`, `swapfile.sys`, or `hiberfil.sys`, wipe unrelated restore/history sets, or modify system databases merely because a selected file may once have been represented there.
- Strong authenticated encryption using AES-256-GCM rather than a Sentinel-specific cipher. Each item uses a fresh cryptographically random data-encryption key, unique nonce material, authenticated metadata, versioned container format, and corruption detection. Large files require a reviewed streaming/chunked format.
- Safe encryption transaction: validate source, create separate encrypted output, complete encryption, flush, reopen/authenticate/verify, and only then permit optional plaintext removal. Encryption or verification failure keeps the original. Plaintext-removal failure keeps the encrypted copy and reports that plaintext remains.
- Windows-account mode uses current-user Windows data protection for wrapping independent file keys rather than machine-wide protection by default.
- Portable password mode uses a maintained reviewed KDF, preferring Argon2id when supportable, with random salt, versioned parameters, no plaintext password storage, and authenticated metadata.
- Independent high-entropy recovery keys support Copy, Save, and Print. Recovery secrets are not silently uploaded/escrowed. Users must understand recovery configuration before `Encrypt + Secure Delete Original` is enabled.
- Sentinel Vault uses a vault master key to wrap independent per-item keys rather than one raw content key for all files. It includes automatic timeout/session locking, Windows-account/password/recovery modes, encrypted sensitive metadata where practical, and optional future Windows Hello/TPM only after dedicated review.
- Avoid plaintext temporary extraction from the vault wherever practical.
- Existing server-side subscription/entitlement architecture remains authoritative for premium access. Explorer contains no reusable entitlement secret, and entitlement never substitutes for filesystem/object safety validation.
- Independent privacy/cryptographic review and Windows runtime/storage validation are mandatory before release.

### Phase F implementation order

1. Complete current production hardening and release qualification.
2. Add and validate harmless `Inspect with Sentinel AI` Explorer integration.
3. Specify and independently review the encrypted-container/recovery format.
4. Implement encryption core.
5. Implement recovery modes.
6. Implement Sentinel Vault.
7. Implement Secure Delete for the selected primary file using proven exact-target safeguards.
8. Add attributable-copy discovery and carefully scoped cleanup.
9. Add advanced privacy options only where safely supported.
10. Complete independent privacy/crypto review and Windows runtime/storage validation.

### Minimum Phase F validation matrix

Encryption: normal encrypt/decrypt, wrong password/account, recovery key, corrupt/truncated ciphertext/metadata, large/empty files, cancellation, crash, disk full, destination collision, concurrency, and format compatibility.

Vault: lock/unlock, automatic/session lock, wrong credentials, recovery, abrupt termination, corrupted metadata, and package upgrade.

Secure Delete/privacy: ordinary/long/Unicode paths, link/reparse/race cases, protected Windows paths, synchronized folders, HDD/SSD/NVMe/BitLocker, cancellation, access denied, crash, unavailable volume, and declined confirmation.

Explorer: clean install, upgrade/uninstall, Explorer restart, multi-select, unsupported item types, and shell-crash resistance.

Subscription: unsubscribed, expired, revoked, tampered capability, backend/offline failure, and Store unavailable.

### Marketing boundary

Do not market Secure Delete as "guaranteed forensically unrecoverable" unless specific platform/media evidence actually proves that claim. Preferred positioning remains `Secure Delete`, `Maximum safe privacy removal`, `Verified removal where Sentinel can prove it`, and `Storage-aware secure deletion`.

## Current Next Milestones

1. Complete exact-head Windows/subprocess/driver/external-research/optimization/package architecture gates and repair any real failures.
2. Finish A21/A22 remaining full-path resource review while keeping CAB extraction disabled.
3. Finish broker packaged/UAC adversarial runtime validation (A07/A15).
4. Complete remaining Ask Sentinel installed/runtime validation (A19).
5. Complete Authenticode extended runtime matrix (A01).
6. Execute AI gateway Google Cloud/Store staging validation (A05).
7. Finish A06/A08/A10/A11/A13/A17 runtime evidence.
8. Complete or safely leave disabled A14/A16.
9. Complete A23/A24/A28 installed runtime validation.
10. Complete A29 release qualification, including real x86/x64/ARM64 runtime qualification for every architecture shipped.
11. Fresh 1-hour/8-hour stability plus High-finding and full 29-finding re-audits.
12. Only then begin Phase F Premium Privacy Protection unless explicitly authorized earlier.

---

End of Document

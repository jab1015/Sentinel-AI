# SAI-005 — Product Roadmap

Version: 2.1  
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

Current accomplishments include bounded subprocess execution, quarantine recovery/cleanup hardening, package payload verification, broker package-identity binding, Authenticode improvements, deterministic DISM/SFC classification, redaction/history/diagnostic/event-filtering tests, and AI gateway security harness coverage.

Exit criteria:

- Every High and Medium finding corrected or explicitly disabled/fail-closed where a safe capability is not ready.
- Required deterministic, adversarial, packaged-runtime, cloud, Store, and architecture-specific evidence attached to each finding.
- Full 29-finding adversarial re-audit completed.

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

Status: **ACTIVE**

- Preserve natural-language investigation and explanations.
- Final displayed answer must pass deterministic claim/provenance validation after every response replacement/composition path.
- Separate VERIFIED FACT, OBSERVED, INFERRED, ACTION VERIFIED, and ADVISORY semantics.
- AI prose must never invent blocked/quarantined/repaired/Defender/firewall outcomes.

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

Status: **PLANNED — DO NOT IMPLEMENT UNTIL CURRENT HARDENING/RELEASE GATES ARE CLOSED**

Subscription-only roadmap:

- File Explorer context-menu integration: Inspect with Sentinel AI, Secure Delete, Encrypt File, Add to Sentinel Vault.
- A thin, non-privileged Explorer extension that only activates Sentinel with selected-item context; all protected work remains in the app/broker architecture.
- Secure Delete for user-selected files using supported Windows/storage mechanisms, with exact target revalidation, protected-location safeguards, media-aware behavior, explicit confirmation, and verification-oriented result reporting.
- Safe discovery of attributable local copies/references such as Sentinel-created artifacts, File History/Previous Versions indicators, search references, temporary/recovery copies, and cloud-sync indicators where attribution is reliable.
- No automatic removal of unrelated system data, broad restore/history sets, or system-managed paging/hibernation files as a side effect of deleting one selected file.
- Strong authenticated file encryption using a reviewed standard design such as AES-256-GCM with a fresh random data key per item and authenticated metadata.
- Windows-account protection using current-user DPAPI key wrapping.
- Portable password protection using a maintained, reviewed password KDF such as Argon2id when supportable.
- Independent high-entropy recovery-key support with explicit user confirmation before optional deletion of the plaintext original.
- Sentinel Vault with a vault master-key hierarchy protecting per-item keys, automatic locking, Windows-session-lock integration, password/account/recovery options, and optional Windows Hello/TPM integration only after dedicated design review.
- Safe encryption transaction: create and verify a new encrypted container before offering to remove the plaintext source; never destroy the only known-good copy if encryption verification fails.
- Existing server-side subscription/entitlement architecture remains authoritative for premium access, while broker/file-identity safety checks remain mandatory regardless of entitlement.
- Independent privacy/crypto review before release, including corruption, wrong-key, recovery, crash, cancellation, large-file, storage-type, BitLocker, package, and upgrade scenarios.

### Phase F implementation order

1. Complete current production hardening and release qualification.
2. Add and validate harmless `Inspect with Sentinel AI` Explorer integration.
3. Design and independently review the encrypted-container/recovery format.
4. Implement encryption and recovery modes.
5. Implement Sentinel Vault.
6. Implement selected-file Secure Delete using the proven broker and exact-target safeguards.
7. Add attributable-copy discovery and carefully scoped cleanup actions.
8. Complete independent privacy/crypto and Windows runtime validation before marketing claims are finalized.

### Result semantics

Privacy features must distinguish:

- **VERIFIED** — Sentinel directly verified the result.
- **REQUESTED** — Sentinel requested a supported Windows/storage action but cannot independently prove lower-level physical effects.
- **REMAINS** — an attributable local/external copy remains.
- **CANNOT PROVE** — the platform or storage hardware does not expose enough information for Sentinel to prove absence.

The product goal is maximum safe privacy protection with transparent evidence, not unsupported guarantees.

## Current Next Milestones

1. Broker packaged/UAC adversarial validation (A07/A15).
2. Final Ask Sentinel claim boundary (A19).
3. Authenticode extended runtime matrix (A01).
4. AI gateway staging/Store validation (A05).
5. Remaining findings and full re-audit.
6. Final release qualification.
7. Only then begin Phase F Premium Privacy Protection.

---

End of Document

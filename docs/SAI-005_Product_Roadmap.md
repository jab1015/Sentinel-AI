# SAI-005 — Product Roadmap

Version: 3.5  
Status: Active — Premium Privacy source/CI qualified; signed LocalDev package and Windows runtime revalidation are the immediate gate  
Last Updated: 2026-09-14

Copyright (c) 2026 Modern Methods.

---

## Product Vision

Sentinel AI will be a trustworthy Windows security and system-assistance platform that continuously monitors verified evidence, explains findings in plain language, detects suspicious behavior, safely contains/remediates supported threats, preserves user control, and never claims an action succeeded without verification.

## Branch Isolation

- Hardening: `security/production-hardening-1218f5d`
- Qualified hardening checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- Hardening automated qualification: **12/12 PASS**
- Premium Privacy: `feature/premium-privacy-foundation`
- Latest source/test-qualified checkpoint before this documentation synchronization: `8899a9d8afa04056b2131a989f4cd6f1c832f801`
- Premium Privacy workflow run `34909736208` (#271): **PASS**
- Current package version: `1.0.27.0`

Neither development branch is authorized for automatic merge to `main`.

## Phase A — Production Security Hardening

Status: **SOURCE/AUTOMATED QUALIFIED — WINDOWS/EXTERNAL VALIDATION REMAINS**

Remaining evidence includes installed UAC/broker, Authenticode, quarantine/recovery, Defender/firewall, devices/drivers, lifecycle, signed package, real architecture runtime, Store/GCP, stability, and final adversarial re-audit.

## Phase B — Active Protection

Status: **IN DEVELOPMENT / NOT RELEASE-QUALIFIED**

Continue toward malicious/suspicious behavior detection, honest-confidence ransomware/malware indicators, warning/explanation, supported blocking/containment, quarantine/restore, network containment, Defender integration, and exact-target elevated remediation while preserving Ask Sentinel and hardware/software monitoring/repair.

## Phase C — Ask Sentinel Trust Boundary

Status: **SOURCE IMPLEMENTED — WINDOWS RUNTIME VALIDATION PENDING**

Displayed answers must remain evidence-bound and may not claim security actions without verified action evidence.

## Phase D — Premium Privacy

Status: **SOURCE/CI FOUNDATION QUALIFIED — WINDOWS RUNTIME REVALIDATION PENDING**

Implemented and source/CI-qualified work includes:

- packaged native `IExplorerCommand` integration;
- compact Explorer-action dialog hosting that does not intentionally surface the full dashboard;
- AES-256-GCM encrypted containers;
- local Windows-profile protection;
- password-protected portable `Encrypt for Sharing...` containers independent of the originating Windows profile;
- verified encryption replacement: encrypted result first, plaintext retirement second;
- portable password retry UX for short/empty/mismatched values;
- authenticated decryption followed by exact-object logical encrypted-source retirement;
- Sentinel Vault cryptographic/storage foundation;
- selectable/copyable Vault recovery-key presentation;
- exact-object logical Secure Delete with durable authorization/journal semantics;
- bounded attributable-copy/history discovery;
- independently authorized related cleanup;
- Premium capability scopes `privacy.encrypt`, `privacy.vault`, `privacy.secure-delete`, and `privacy.discovery`;
- persistent optimization baseline with stale/future/corrupt-state defenses.

Explorer carries intent/selection only. Fuzzy similarity never authorizes deletion. Existing encrypted/Vault data remains recoverable without a new paid entitlement.

### Encryption Replacement — Completed in Source/CI

Both local and portable encryption now:

1. create/finalize the encrypted container;
2. verify successful authenticated output;
3. retire plaintext only after verification;
4. provide no `Keep original` choice;
5. preserve plaintext on encryption, verification, cancellation, or retirement failure;
6. refuse to report full replacement success when plaintext retirement is incomplete;
7. preserve destination-collision protections;
8. preserve legacy `.sentinel.senc` compatibility.

Acceptance coverage includes success, failure, unverified output, cancellation, collision, source-retirement failure, portable round-trip, and legacy v1 decryption.

### Vault Scope

Vault has a substantial tested source/backend foundation, but the complete user-facing Vault experience remains **unfinished**. Do not promote Vault to complete until its full UI/workflow and Windows runtime matrix are finished.

## Phase E — Optimization Baseline Reliability

Status: **SOURCE/CI QUALIFIED — INSTALLED RUNTIME REVALIDATION PENDING**

The baseline no longer resets on every app restart. Persisted history is stored in Local AppData and guarded by the existing one-minute accepted-sample cadence plus new validity checks.

Qualified behavior includes:

- 12 accepted samples to establish a baseline;
- at most one accepted sample per minute;
- 720-sample cap;
- persistence across restart;
- rejection of persisted samples older than 24 hours;
- rejection of samples more than five minutes in the future;
- corrupt-state fallback to relearning;
- non-finite metric sanitization;
- recovery from malicious/invalid future timestamps without suppressing new legitimate observations.

Manual optimization remains a scan/evaluation action. Automatic optimization remains subscription/safety/state/cooldown controlled.

## Phase F — Windows VM Test Package

Status: **FRESH SIGNED LOCALDEV PACKAGE QUALIFICATION IN PROGRESS**

Authoritative evidence: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

Exact-head Premium Privacy workflow `34909736208` passed on `8899a9d8...`.

Dedicated VM package run `34908583736` (#69) packages app source `a12af475b7212edf1d78e37fbcf07c9fb0bc34dd`. It has passed LocalDev entitlement-boundary verification and authoritative Release x64 entitlement compilation; the build/sign/qualify step is in progress at this synchronization.

Earlier package run `34901580179` (#68) was superseded after the package build/sign/qualify step had completed successfully, but artifact upload was cancelled. This is supporting evidence, not a final artifact qualification.

Package version remains `1.0.27.0`. Do not weaken signing, identity, Publisher, package-content, manifest, architecture, or entitlement checks to obtain green CI.

## Phase G — Windows Physical Validation

Status: **ACTIVE / NOT COMPLETE**

Using the fresh qualified package, execute clean install/upgrade/uninstall; Explorer restart/context-menu and compact-dialog cases; standard/admin/UAC; encryption replacement success/failure/collision; portable password retry and cross-machine/profile recovery; decryption encrypted-source retirement; tamper/crash cases; Vault recovery-key copyability and lifecycle; Secure Delete race/reparse/hardlink/protected-path/crash cases; optimization-baseline restart persistence; discovery providers; Defender/firewall; startup/background/sleep/wake/network loss; quarantine/recovery; and stability/resource validation.

Any runtime defect must be corrected at root cause with regression coverage and applicable CI requalification.

## Phase H — Cloud and Store External Qualification

Status: **OPEN IN PARALLEL**

Microsoft Store remains authoritative for paid status (`sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`). The Google Cloud gateway remains the server-authoritative premium enforcement boundary.

Still required are shared atomic multi-instance replay/rate/concurrency state, GCP staging with Secret Manager/least-privilege IAM/restart/failover/safe logging/alerts/budget controls, and real Microsoft Store active/inactive/expired/revoked/unavailable entitlement evidence.

## Phase I — Final Release Qualification

Status: **NOT READY**

After Windows and external qualification, complete fresh final-commit 1-hour/8-hour stability/resource runs, privacy/security review, High-finding re-audit, full 29-finding re-audit, and final signed distribution evidence. Only then consider a production merge/release decision.

## Immediate Next Milestones

1. Complete dedicated signed LocalDev x64 package qualification and capture the downloadable artifact.
2. Install that exact package on the Windows 11 VM and execute the corrected Explorer/privacy/optimization runtime matrix.
3. Fix any proven runtime defect at root cause and add regression coverage.
4. Preserve package version `1.0.27.0`, production identity/Publisher, compile-time-only LocalDev entitlement, and Release entitlement behavior.
5. Continue cloud/Store external qualification.
6. Complete final stability and independent re-audits.
7. Only after all gates pass, consider final independent review and a separate merge decision.

## Current Decision

- HARDENING SOURCE/AUTOMATED: **PASS**
- PREMIUM PRIVACY SOURCE/CI: **PASS at `8899a9d8...` / run `34909736208`**
- ENCRYPTION REPLACEMENT SOURCE/CI: **PASS**
- OPTIMIZATION BASELINE PERSISTENCE SOURCE/CI: **PASS**
- FRESH AUTOMATED SIGNED VM PACKAGE: **IN PROGRESS — run `34908583736`**
- WINDOWS RUNTIME QUALIFICATION: **NOT COMPLETE**
- VAULT USER-FACING COMPLETION: **NO**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- RELEASE QUALIFIED: **NO**

---

End of Document

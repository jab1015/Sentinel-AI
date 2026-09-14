# SAI-005 — Product Roadmap

Version: 3.4  
Status: Active — Premium Privacy source qualified; LocalDev VM/runtime validation and encryption UX correction are next  
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
- Exact-head source/CI checkpoint before this documentation synchronization: `c0b60c06e4f03f9486965c799f7af028dd450a05`
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

Status: **SOURCE/CI FOUNDATION QUALIFIED — WINDOWS RUNTIME VALIDATION PENDING**

Implemented foundation includes:

- packaged native `IExplorerCommand` integration;
- AES-256-GCM encrypted containers;
- local Windows-profile protection;
- password-protected portable `Encrypt for Sharing...` containers independent of the originating Windows profile;
- Sentinel Vault foundation;
- exact-object logical Secure Delete with durable authorization/journal semantics;
- bounded attributable-copy/history discovery;
- independently authorized related cleanup;
- Premium capability scopes `privacy.encrypt`, `privacy.vault`, `privacy.secure-delete`, and `privacy.discovery`.

Explorer carries intent/selection only. Fuzzy similarity never authorizes deletion. Existing encrypted/Vault data remains recoverable without a new paid entitlement.

### Approved Encryption UX Correction

Current source creates `.sentinel.senc` successfully but leaves the plaintext original untouched. That is no longer the desired product behavior.

Required next behavior for both local and portable encryption:

1. create the encrypted container;
2. verify successful finalization/authenticated container creation;
3. automatically remove the original plaintext source;
4. provide no `Keep original` choice;
5. preserve the source on any encryption/verification failure;
6. preserve backward compatibility with existing `.sentinel.senc` files.

This is a required source/test change, not yet completed at checkpoint `c0b60c06...`.

## Phase E — Windows VM Test Package

Status: **MANUAL LOCALDEV BUILD PASS; AUTOMATED SIGNED-PACKAGE QUALIFICATION STILL OPEN**

Authoritative evidence: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

Exact-head Premium Privacy workflow `34792512987` passed on `c0b60c06...`.

Dedicated VM package workflow `34792512975` on the same SHA passed LocalDev entitlement verification and Release x64 compilation but was cancelled after the build/sign/qualify package step remained active until the workflow limit.

A manual Visual Studio rebuild of the same source using `LocalDev | x64` completed successfully with `2 succeeded, 0 failed, 1 up-to-date, 0 skipped`. Manual MSIX creation then began for isolated VM testing.

Manual VM testing is useful runtime evidence, but it does not turn the cancelled automated packaging workflow into a pass and is not Store/production signing evidence.

## Phase F — Windows Physical Validation

Status: **ACTIVE / NOT COMPLETE**

Execute clean install/upgrade/uninstall; Explorer restart/context-menu and selection cases; standard/admin/UAC; encryption/recovery/corruption/crash; portable sharing; Vault lifecycle/recovery/concurrency; Secure Delete race/reparse/hardlink/protected-path/crash cases; discovery providers; Defender/firewall; startup/background/sleep/wake/network loss; quarantine/recovery; and architecture/runtime validation.

After the encryption UX correction, explicitly prove that plaintext is removed only after successful verified encryption and preserved on every failure path.

Any runtime defect must be corrected at root cause with regression coverage and applicable CI requalification.

## Phase G — Cloud and Store External Qualification

Status: **OPEN IN PARALLEL**

Microsoft Store remains authoritative for paid status (`sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`). The Google Cloud gateway remains the server-authoritative premium enforcement boundary.

Still required are shared atomic multi-instance replay/rate/concurrency state, GCP staging with Secret Manager/least-privilege IAM/restart/failover/safe logging/alerts/budget controls, and real Microsoft Store active/inactive/expired/revoked/unavailable entitlement evidence.

## Phase H — Final Release Qualification

Status: **NOT READY**

After Windows and external qualification, complete final 1-hour/8-hour stability/resource runs, privacy/security review, High-finding re-audit, full 29-finding re-audit, and final signed distribution evidence. Only then consider a production merge/release decision.

## Immediate Next Milestones

1. Implement the approved encryption replacement behavior and regression coverage.
2. Keep package version `1.0.27.0` and preserve production identity/Publisher.
3. Complete the LocalDev x64 MSIX and Windows 11 VM install/runtime matrix one step at a time.
4. Diagnose and fix the automated VM-package workflow stall without weakening verification.
5. Re-run applicable exact-head CI after any source change.
6. Continue cloud/Store external qualification.
7. Complete stability and independent re-audits.

## Current Decision

- PREMIUM PRIVACY SOURCE/CI: **PASS at c0b60c06...**
- MANUAL LOCALDEV X64 BUILD: **PASS**
- AUTOMATED SIGNED VM PACKAGE: **NOT QUALIFIED**
- WINDOWS RUNTIME QUALIFICATION: **NOT COMPLETE**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- RELEASE QUALIFIED: **NO**

---

End of Document

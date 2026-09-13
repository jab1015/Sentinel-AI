# SAI-005 — Product Roadmap

Version: 3.3  
Status: Active — source/CI qualified; Windows VM package qualification is the immediate gate  
Last Updated: 2026-09-13

Copyright (c) 2026 Modern Methods.

---

## Product Vision

Sentinel AI will be a trustworthy Windows security and system-assistance platform that continuously monitors verified evidence, explains findings in plain language, detects suspicious behavior, safely contains/remediates supported threats, preserves user control, and never claims an action succeeded without verification.

## Branch Isolation

- Hardening: `security/production-hardening-1218f5d`
- Qualified hardening checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- Hardening automated qualification: **12/12 PASS**
- Premium Privacy: `feature/premium-privacy-foundation`
- VM package source checkpoint currently under qualification: `f51a31fe53c64cadd0e346eb32648a86353d23f2`
- Current package version: `1.0.26.0`

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
- AES-256-GCM encrypted containers and recovery modes;
- Sentinel Vault;
- exact-object logical Secure Delete with durable authorization/journal semantics;
- bounded attributable-copy/history discovery;
- independently authorized related cleanup;
- Premium capability scopes `privacy.encrypt`, `privacy.vault`, `privacy.secure-delete`, and `privacy.discovery`.

Explorer carries intent/selection only. Fuzzy similarity never authorizes deletion. Existing encrypted/Vault data remains recoverable without a new paid entitlement.

## Phase E — Windows VM Test Package

Status: **ACTIVE — BLOCKED AT AUTOMATED SIGNATURE VERIFICATION**

Authoritative evidence: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

Latest run `34736783747` / job `103669514840` against source `f51a31fe53c64cadd0e346eb32648a86353d23f2` successfully completed Release x64 MSIX build, ephemeral test-certificate creation, and MSIX signing. The signature-verification step remained active until the 30-minute job timeout and was cancelled.

Next milestone is not a manual Visual Studio package. The required milestone is one reproducible GitHub workflow run that successfully:

1. builds Release/x64 MSIX;
2. signs with dedicated ephemeral VM test signing material;
3. cryptographically verifies the signature and exact signer;
4. verifies package identity/Publisher/x64 architecture;
5. verifies required app, privileged broker, and Explorer extension binaries exactly once;
6. verifies Explorer COM/context-menu registrations;
7. proves no private signing key is in the package/artifact;
8. publishes the public certificate, install/uninstall helpers, metadata, and SHA-256;
9. survives independent downloaded-artifact inspection.

Only then may `WINDOWS VM TESTING READY` become YES.

## Phase F — Windows Physical Validation

Status: **WAITING FOR QUALIFIED VM PACKAGE**

After the package gate passes, execute clean install/upgrade/uninstall; Explorer restart/context-menu and selection cases; standard/admin/UAC; encryption/recovery/corruption/crash; Vault lifecycle/recovery/concurrency; Secure Delete race/reparse/hardlink/protected-path/crash cases; File History/Previous Versions/Search/Recent/Jump Lists/OneDrive discovery; Defender/firewall; startup/background/sleep/wake/network loss; quarantine/recovery; and architecture/runtime validation.

Any runtime defect must be corrected at root cause with regression coverage and applicable CI requalification.

## Phase G — Cloud and Store External Qualification

Status: **OPEN IN PARALLEL**

Microsoft Store remains authoritative for paid status (`sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`). The Google Cloud gateway remains the server-authoritative premium enforcement boundary.

Still required are shared atomic multi-instance replay/rate/concurrency state, GCP staging with Secret Manager/least-privilege IAM/restart/failover/safe logging/alerts/budget controls, and real Microsoft Store active/inactive/expired/revoked/unavailable entitlement evidence.

## Phase H — Final Release Qualification

Status: **NOT READY**

After Windows and external qualification, complete final 1-hour/8-hour stability/resource runs, privacy/security review, High-finding re-audit, full 29-finding re-audit, and final signed distribution evidence. Only then consider a production merge/release decision.

## Immediate Next Milestones

1. Diagnose exact blocking operation in VM package signature verification.
2. Fix verification/timeout handling without weakening cryptographic validation.
3. Obtain one fully successful package workflow and inspect its downloaded artifact.
4. Record exact package source SHA, certificate, artifact and SHA-256.
5. Begin Windows 11 VM installation one step at a time.
6. Execute Windows runtime matrices while cloud/Store work proceeds in parallel.
7. Complete stability and independent re-audits.

## Current Decision

- WINDOWS VM TESTING READY: **NO**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- RELEASE QUALIFIED: **NO**

---

End of Document

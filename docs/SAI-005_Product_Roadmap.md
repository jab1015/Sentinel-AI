# SAI-005 — Product Roadmap

Version: 3.6  
Status: Active — VM UX/Vault completion and responsive visual modernization are immediate milestones  
Last Updated: 2026-09-14

Copyright (c) 2026 Modern Methods.

---

## Product Vision

Sentinel AI will be a trustworthy Windows security and system-assistance platform that continuously monitors verified evidence, explains findings in plain language, detects suspicious behavior, safely contains/remediates supported threats, preserves user control, and never claims an action succeeded without verification.

The user experience should match that trust model: clear, compact, modern, responsive, and visually consistent with Modern Methods/Sentinel branding rather than looking like a collection of legacy utility dialogs.

## Branch Isolation

- Hardening: `security/production-hardening-1218f5d`
- Qualified hardening checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- Hardening automated qualification: **12/12 PASS**
- Premium Privacy: `feature/premium-privacy-foundation`
- Current implementation checkpoint before this documentation update: `3cd7a0da195145d03cee3aa947d3f0b9c1d8f69f`
- Current Premium Privacy workflow: `34913034860` (#279), **IN PROGRESS**
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

Status: **FOUNDATION SUBSTANTIAL — VAULT/UX COMPLETION AND FRESH EXACT-HEAD QUALIFICATION REQUIRED**

Implemented foundation includes native Explorer integration, AES-256-GCM encrypted containers, Windows-profile protection, portable password encryption, verified encryption replacement, authenticated recovery, Secure Delete exact-object infrastructure, privacy discovery, premium capability scopes, and Vault cryptographic/storage foundations.

### Explorer UX Milestone

Explorer actions must be compact, branded and task-focused. The current large gray host surrounding a white dialog is not the desired final experience.

Required outcomes:

- Inspect, Encrypt, Decrypt, Vault and Secure Delete flows open only the useful compact Sentinel surface;
- visual styling uses Sentinel/Modern Methods navy, electric blue, cyan, white and restrained security-state colors;
- action surfaces scale for DPI/text-size changes and do not require a full dashboard window;
- Inspect results communicate `No issue found` / `No suspicious condition found` when appropriate, while accurately describing what was actually checked;
- no unsupported `safe from malware` statement is made when the inspection only verified filesystem/accessibility properties.

### Encryption Replacement

Both local and portable encryption retain the approved safety order:

1. create/finalize encrypted output;
2. verify authenticated output;
3. retire plaintext only after verification;
4. preserve plaintext on failure/cancellation/incomplete retirement;
5. never report full replacement success when plaintext remains unexpectedly;
6. preserve collision and legacy-container protections.

### Vault Completion Milestone

Vault is now a priority product-completion item rather than only a backend foundation.

Required user-facing capabilities:

- visible Vault entry in the primary application navigation;
- Vault dashboard/browser listing committed items;
- Add Files;
- Add Folder with safe recursive enumeration and clear partial-failure reporting;
- verified encrypted commit followed by exact-source retirement so a successful Vault move does not leave a readable duplicate behind;
- fail-safe preservation of the source if commit/verification/retirement cannot be proven;
- restore/export and recovery UX;
- item metadata useful enough to identify/manage stored content without exposing plaintext unnecessarily;
- refresh, empty state, progress and error states;
- recovery key copy/paste;
- cancellation/crash/recovery behavior;
- regression coverage for reparse points, hard links, protected locations, identity races and collisions.

A new `VaultSourceRetirementService` and acceptance harness establish the initial exact-object retirement boundary. This implementation is not called qualified until current exact-head CI passes and installed VM testing confirms behavior.

## Phase E — Responsive Visual Modernization

Status: **ACTIVE**

Sentinel's main application should feel closer to a modern responsive web application while remaining native WinUI 3.

Design direction:

- responsive `VisualStateManager` breakpoints rather than relying on one fixed desktop composition;
- clean scaling at compact, normal, wide and maximized window sizes;
- Modern Methods/Sentinel navy + electric-blue + cyan palette with white/light surfaces where appropriate;
- consistent card radii, spacing, typography and hierarchy;
- clearer security state presentation;
- reduced visual clutter and oversized empty areas;
- obvious primary actions;
- accessible contrast, keyboard focus and Windows text scaling;
- no loss of native Windows behavior merely to imitate HTML/CSS.

The current branch includes the first dashboard/navigation visual refresh and visible Vault entry. Further page-by-page modernization remains planned after functional Vault/Explorer correctness is secured.

## Phase F — Optimization Baseline Reliability

Status: **IMPLEMENTED — FRESH EXACT-HEAD/INSTALLED REVALIDATION REQUIRED**

The baseline persists in Local AppData with 12-sample establishment, one accepted sample/minute, 720-sample cap, stale/future/corrupt-state defenses and non-finite metric sanitization. Manual optimization remains scan/evaluation; automatic execution remains subscription/safety/state/cooldown controlled.

## Phase G — Windows VM Test Package

Status: **CURRENT-HEAD PACKAGE NOT YET QUALIFIED**

Authoritative evidence: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

The previous package was useful for installed VM discovery but is superseded by current UI/Vault source changes. A new signed LocalDev x64 package must be generated from a source head whose required workflows pass.

Package version remains `1.0.27.0`. Signing, identity, Publisher, package-content, manifest, architecture and entitlement checks must not be weakened.

## Phase H — Windows Physical Validation

Status: **ACTIVE / NOT COMPLETE**

The next VM matrix must include compact Explorer dialogs; security-oriented Inspect wording; encryption/decryption replacement and recovery; Vault launch/browser/Add Files/Add Folder/source-retirement/restore/recovery; responsive dashboard behavior; Secure Delete adversarial cases; optimization persistence; discovery providers; Defender/firewall; startup/background/sleep/wake/network loss; quarantine/recovery; UAC/admin/standard-user behavior; and stability/resource validation.

Any runtime defect must be corrected at root cause with regression coverage and applicable CI requalification.

## Phase I — Cloud and Store External Qualification

Status: **OPEN IN PARALLEL**

Microsoft Store remains authoritative for paid status (`sentinel-ai-monthly`, Store ID `9N67THV2Z1GP`). The Google Cloud gateway remains the server-authoritative premium enforcement boundary.

Still required are shared atomic multi-instance replay/rate/concurrency state, GCP staging with Secret Manager/least-privilege IAM/restart/failover/safe logging/alerts/budget controls, and real Microsoft Store active/inactive/expired/revoked/unavailable entitlement evidence.

## Phase J — Final Release Qualification

Status: **NOT READY**

After Windows and external qualification, complete fresh final-commit 1-hour/8-hour stability/resource runs, privacy/security review, High-finding re-audit, full 29-finding re-audit, and final signed distribution evidence. Only then consider a production merge/release decision.

## Immediate Next Milestones

1. Finish current exact-head Premium Privacy CI and correct any regression without weakening tests.
2. Complete compact Explorer dialog visual refinement and evidence-accurate Inspect wording.
3. Complete the user-facing Vault browser, Add Files replacement, Add Folder import and recovery/restore workflow.
4. Continue responsive page-by-page Sentinel visual modernization using the Modern Methods/Sentinel palette.
5. Produce a fresh signed LocalDev x64 MSIX from the qualified current head while preserving version `1.0.27.0`.
6. Re-run the full installed Windows VM matrix against that exact artifact.
7. Continue cloud/Store external qualification and final stability/re-audits.
8. Only after all gates pass, consider final independent review and a separate merge decision.

## Current Decision

- HARDENING SOURCE/AUTOMATED: **PASS**
- CURRENT PREMIUM PRIVACY EXACT-HEAD CI: **IN PROGRESS — run `34913034860`**
- ENCRYPTION REPLACEMENT: **IMPLEMENTED; fresh qualification required**
- OPTIMIZATION BASELINE: **IMPLEMENTED; fresh qualification required**
- VAULT USER-FACING COMPLETION: **NO — ACTIVE PRIORITY**
- RESPONSIVE VISUAL MODERNIZATION: **ACTIVE**
- CURRENT-HEAD SIGNED VM PACKAGE: **NOT YET QUALIFIED**
- WINDOWS RUNTIME QUALIFICATION: **NOT COMPLETE**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- RELEASE QUALIFIED: **NO**

---

End of Document
